using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TeamPilot.Application.Agents;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Commits.Dtos;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Git;
using TeamPilot.Application.Instructions;
using TeamPilot.Application.Llm;
using TeamPilot.Application.Orchestration.Dtos;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Tickets;
using TeamPilot.Application.Tickets.Dtos;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using TeamPilot.Domain.Exceptions;

namespace TeamPilot.Application.Orchestration;

/// <summary>
/// Runs every ticket through the same fixed pipeline: Research -&gt; Design -&gt; Coding -&gt; Testing,
/// using the project's single active agent for each of those four roles (see
/// <see cref="IAgentService.EnsureDefaultAgentsAsync"/>). When Testing reports a failure, Coding
/// is re-run with the failure fed back in as context, bounded by <see cref="MaxAttempts"/>.
/// </summary>
public sealed class OrchestrationService(
    ITicketRepository ticketRepository,
    IAgentRepository agentRepository,
    IAgentService agentService,
    IProjectRepository projectRepository,
    IGitService gitService,
    IGitCredentialProtector credentialProtector,
    ILlmConnector llmConnector,
    IInstructionRepository instructionRepository,
    IProjectAccessGuard projectAccessGuard,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork,
    ILogger<OrchestrationService> logger) : IOrchestrationService
{
    private const int MaxAttempts = 3;

    private static readonly Regex TestingVerdictPattern =
        new(@"RESULT:\s*(PASS|FAIL)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<TicketPipelineResultDto> RunPipelineAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        var ticket = await ticketRepository.GetByIdAsync(ticketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), ticketId);

        await projectAccessGuard.EnsureAccessAsync(ticket.ProjectId, cancellationToken);

        var project = await projectRepository.GetByIdAsync(ticket.ProjectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), ticket.ProjectId);

        await agentService.EnsureDefaultAgentsAsync(project.Id, cancellationToken);

        var researchAgent = await GetPipelineAgentAsync(project.Id, AgentRole.Research, cancellationToken);
        var designAgent = await GetPipelineAgentAsync(project.Id, AgentRole.Design, cancellationToken);
        var codingAgent = await GetPipelineAgentAsync(project.Id, AgentRole.Coding, cancellationToken);
        var testingAgent = await GetPipelineAgentAsync(project.Id, AgentRole.Testing, cancellationToken);

        ticket.AssignAgent(researchAgent);
        ticket.AssignAgent(designAgent);
        ticket.AssignAgent(codingAgent);
        ticket.AssignAgent(testingAgent);

        if (string.IsNullOrWhiteSpace(ticket.BranchName))
        {
            await LinkBranchAsync(ticket, project, cancellationToken);
        }

        await auditLogger.LogActionAsync(AuditEventType.TicketPipelineStarted, $"Pipeline started for ticket '{ticket.Title}'.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var steps = new List<AgentWorkResultDto>();

        // Both the instructions block and the cancellation check are re-read from the database
        // immediately before each stage runs (rather than once, up front) so an edit to an
        // agent's instructions - or cancelling the ticket outright - made while this same ticket
        // is mid-pipeline still takes effect on whichever stage hasn't started yet, including a
        // later Coding retry after a Testing failure. This can't interrupt an LLM call already
        // in flight, only stop the pipeline from moving on to the next one.
        await EnsureNotCancelledAsync(ticket.Id, cancellationToken);
        var researchInstructions = await GetInstructionsBlockAsync(researchAgent.Id, cancellationToken);
        var researchOutput = await RunPromptOnlyStageAsync(ticket, researchAgent, BuildResearchPrompt(ticket, researchInstructions), steps, cancellationToken);

        await EnsureNotCancelledAsync(ticket.Id, cancellationToken);
        var designInstructions = await GetInstructionsBlockAsync(designAgent.Id, cancellationToken);
        var designOutput = await RunPromptOnlyStageAsync(ticket, designAgent, BuildDesignPrompt(ticket, researchOutput, designInstructions), steps, cancellationToken);

        var testingPassed = false;
        var testingAttempts = 0;
        string? previousTestingOutput = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await EnsureNotCancelledAsync(ticket.Id, cancellationToken);
            var codingInstructions = await GetInstructionsBlockAsync(codingAgent.Id, cancellationToken);
            var codingPrompt = BuildCodingPrompt(ticket, designOutput, previousTestingOutput, codingInstructions);
            var (codingOutput, commit) = await RunCodingStageAsync(ticket, project, codingAgent, codingPrompt, attempt, cancellationToken);
            steps.Add(new AgentWorkResultDto(ticket.Id, codingAgent.Id, codingAgent.Role, codingOutput, commit));

            await EnsureNotCancelledAsync(ticket.Id, cancellationToken);
            var testingInstructions = await GetInstructionsBlockAsync(testingAgent.Id, cancellationToken);
            var testingPrompt = BuildTestingPrompt(ticket, codingOutput, testingInstructions);
            var testingResponse = await llmConnector.SendPromptAsync(new LlmRequest(testingPrompt), cancellationToken);
            testingAttempts = attempt;
            previousTestingOutput = testingResponse.Content;
            testingPassed = ParseTestingVerdict(testingResponse.Content);
            steps.Add(new AgentWorkResultDto(ticket.Id, testingAgent.Id, testingAgent.Role, testingResponse.Content, null));

            logger.LogInformation(
                "Testing agent {AgentId} verdict for ticket {TicketId}, attempt {Attempt}/{MaxAttempts}: {Verdict}",
                testingAgent.Id,
                ticket.Id,
                attempt,
                MaxAttempts,
                testingPassed ? "PASS" : "FAIL");

            if (testingPassed)
            {
                break;
            }
        }

        ticket.MoveToReview();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new TicketPipelineResultDto(TicketMappings.ToDto(ticket), steps, testingPassed, testingAttempts);
    }

    private async Task<Agent> GetPipelineAgentAsync(Guid projectId, AgentRole role, CancellationToken cancellationToken)
    {
        return await agentRepository.GetByProjectAndRoleAsync(projectId, role, cancellationToken)
            ?? throw new InvalidOperationException($"Project '{projectId}' has no active {role} agent.");
    }

    /// <summary>
    /// Stops the pipeline from proceeding to the next stage if the ticket was cancelled by a
    /// concurrent request while this run was in flight. Reads the status directly off
    /// <see cref="ITicketRepository.GetStatusAsync"/> rather than the <see cref="Ticket"/>
    /// instance this method already holds, since that instance was loaded once at the start of
    /// this run and this same <c>DbContext</c>'s identity map would otherwise keep returning it
    /// unchanged instead of seeing the other request's update.
    /// </summary>
    private async Task EnsureNotCancelledAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        var currentStatus = await ticketRepository.GetStatusAsync(ticketId, cancellationToken);
        if (currentStatus == TicketStatus.Cancelled)
        {
            throw new InvalidTicketStateTransitionException(TicketStatus.Cancelled, "continue the pipeline");
        }
    }

    private Task<string?> GetInstructionsBlockAsync(Guid agentId, CancellationToken cancellationToken) =>
        AgentInstructionsFormatter.GetInstructionsBlockAsync(instructionRepository, agentId, cancellationToken);

    private async Task<string> RunPromptOnlyStageAsync(
        Ticket ticket,
        Agent agent,
        string prompt,
        List<AgentWorkResultDto> steps,
        CancellationToken cancellationToken)
    {
        var response = await llmConnector.SendPromptAsync(new LlmRequest(prompt), cancellationToken);

        logger.LogInformation(
            "Agent {AgentId} ({Role}) produced output for ticket {TicketId} in project {ProjectId}: {Output}",
            agent.Id,
            agent.Role,
            ticket.Id,
            ticket.ProjectId,
            response.Content);

        steps.Add(new AgentWorkResultDto(ticket.Id, agent.Id, agent.Role, response.Content, null));

        return response.Content;
    }

    private async Task<(string LlmOutput, CommitDto Commit)> RunCodingStageAsync(
        Ticket ticket,
        Project project,
        Agent agent,
        string prompt,
        int attempt,
        CancellationToken cancellationToken)
    {
        var response = await llmConnector.SendPromptAsync(new LlmRequest(prompt), cancellationToken);

        var message = attempt == 1
            ? $"{agent.Role} agent work for '{ticket.Title}'"
            : $"{agent.Role} agent work for '{ticket.Title}' (attempt {attempt})";
        var relativeFilePath = $"tickets/{ticket.Id}.md";

        var commitResult = await gitService.CommitFileAsync(
            project.RepositoryPath,
            ticket.BranchName!,
            relativeFilePath,
            response.Content,
            message,
            agent.Name,
            cancellationToken);

        var accessToken = credentialProtector.Unprotect(project.EncryptedAccessToken);
        await gitService.PushAsync(project.RepositoryPath, ticket.BranchName!, accessToken, cancellationToken);

        var commit = Commit.Create(ticket.Id, ticket.BranchName!, commitResult.CommitHash, message, commitResult.DiffContent, agent.Id);
        ticket.AddCommit(commit);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var commitDto = new CommitDto(commit.Id, commit.TicketId, commit.BranchName, commit.CommitHash, commit.Message, commit.DiffContent, commit.AuthorAgentId, commit.CreatedAtUtc);

        return (response.Content, commitDto);
    }

    private async Task LinkBranchAsync(Ticket ticket, Project project, CancellationToken cancellationToken)
    {
        var branchName = GenerateBranchName(ticket);
        var accessToken = credentialProtector.Unprotect(project.EncryptedAccessToken);

        // Fetch first so the new branch is cut from the remote's current tip of the base
        // branch, not a possibly-stale local one.
        await gitService.FetchAsync(project.RepositoryPath, accessToken, cancellationToken);
        await gitService.EnsureBranchAsync(project.RepositoryPath, branchName, project.BaseBranch, cancellationToken);
        await gitService.PushAsync(project.RepositoryPath, branchName, accessToken, cancellationToken);

        ticket.LinkBranch(branchName);
    }

    // The ticket's Id is the only unique "number" a ticket has today (no sequential per-project
    // numbering exists) - the branch is named after it directly, with no title slug or prefix.
    private static string GenerateBranchName(Ticket ticket) => ticket.Id.ToString("N")[..8];

    private static string BuildResearchPrompt(Ticket ticket, string? instructions) =>
        $"{AgentInstructionsFormatter.FormatInstructions(instructions)}You are the Research agent working on ticket '{ticket.Title}'. Description: {ticket.Description}\n\n" +
        "Investigate and summarize relevant context, constraints, and open questions before any design or implementation work begins.";

    private static string BuildDesignPrompt(Ticket ticket, string researchOutput, string? instructions) =>
        $"{AgentInstructionsFormatter.FormatInstructions(instructions)}You are the Design agent working on ticket '{ticket.Title}'. Description: {ticket.Description}\n\n" +
        $"Research findings:\n{researchOutput}\n\n" +
        "Based on this research, produce a concrete, reviewable design for the Coding agent to implement.";

    private static string BuildCodingPrompt(Ticket ticket, string designOutput, string? previousTestingOutput, string? instructions)
    {
        var prompt = $"{AgentInstructionsFormatter.FormatInstructions(instructions)}You are the Coding agent working on ticket '{ticket.Title}'. Description: {ticket.Description}\n\n" +
            $"Design:\n{designOutput}\n\n";

        if (previousTestingOutput is not null)
        {
            prompt += $"The previous implementation failed testing. Fix these issues:\n{previousTestingOutput}\n\n";
        }

        prompt += "Implement the design as working code.";

        return prompt;
    }

    private static string BuildTestingPrompt(Ticket ticket, string codingOutput, string? instructions) =>
        $"{AgentInstructionsFormatter.FormatInstructions(instructions)}You are the Testing agent working on ticket '{ticket.Title}'. Description: {ticket.Description}\n\n" +
        $"Implementation:\n{codingOutput}\n\n" +
        "Verify the implementation against the ticket's requirements. End your response with a " +
        "line reading exactly 'RESULT: PASS' or 'RESULT: FAIL'.";

    private static bool ParseTestingVerdict(string testingOutput)
    {
        var matches = TestingVerdictPattern.Matches(testingOutput);
        if (matches.Count == 0)
        {
            return true;
        }

        return string.Equals(matches[^1].Groups[1].Value, "PASS", StringComparison.OrdinalIgnoreCase);
    }
}
