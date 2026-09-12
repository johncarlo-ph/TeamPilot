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
using TeamPilot.Application.Workflow;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using TeamPilot.Domain.Exceptions;

namespace TeamPilot.Application.Orchestration;

/// <summary>
/// Runs a ticket through its project's admin-configurable agent workflow (see
/// <see cref="IWorkflowService"/>) - by default Research -&gt; Design -&gt; Coding -&gt; Testing,
/// with Testing looping back to Coding (bounded at 3 attempts), but any sequence, agent set, or
/// loop-back an admin has configured. When a stage with a loop-back reports failure, execution
/// jumps back to its target stage, bounded by that stage's own <c>MaxLoopIterations</c>.
/// </summary>
public sealed class OrchestrationService(
    ITicketRepository ticketRepository,
    IWorkflowStageRepository workflowStageRepository,
    IAgentRepository agentRepository,
    IWorkflowService workflowService,
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
    private static readonly Regex VerdictPattern =
        new(@"RESULT:\s*(PASS|FAIL)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<TicketPipelineResultDto> RunPipelineAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        var ticket = await ticketRepository.GetByIdAsync(ticketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), ticketId);

        await projectAccessGuard.EnsureAccessAsync(ticket.ProjectId, cancellationToken);

        var project = await projectRepository.GetByIdAsync(ticket.ProjectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), ticket.ProjectId);

        await workflowService.EnsureDefaultWorkflowAsync(project.Id, cancellationToken);

        var stages = await workflowStageRepository.ListOrderedAsync(project.Id, cancellationToken);
        if (stages.Count == 0)
        {
            throw new InvalidOperationException($"Project '{project.Id}' has no workflow stages configured.");
        }

        var agentsById = (await agentRepository.ListAsync(project.Id, cancellationToken)).ToDictionary(a => a.Id);

        foreach (var agentId in stages.Select(s => s.AgentId).Distinct())
        {
            ticket.AssignAgent(agentsById[agentId]);
        }

        if (string.IsNullOrWhiteSpace(ticket.BranchName))
        {
            await LinkBranchAsync(ticket, project, cancellationToken);
        }

        await auditLogger.LogActionAsync(AuditEventType.TicketPipelineStarted, $"Pipeline started for ticket '{ticket.Title}'.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var steps = new List<AgentWorkResultDto>();
        var stageRunCounts = new Dictionary<Guid, int>();
        string? previousOutput = null;

        // The final verdict reported on TicketPipelineResultDto - whichever loop-bounded stage
        // ran last (Testing, for the default workflow). A workflow with no loop-back stage at
        // all reports the "nothing to retry" default of passed/zero-attempts.
        var finalVerdictPassed = true;
        var finalVerdictAttempts = 0;

        // A loop-back can only ever target a strictly earlier Order (enforced when the loop-back
        // is configured - see WorkflowService.SetLoopBackAsync), and each stage's own
        // MaxLoopIterations bounds how many times *that* stage may run in total. Total stage
        // executions per run are therefore bounded by stages.Count + sum(MaxLoopIterations),
        // guaranteeing termination without an extra hard cap here.
        var stageIndex = 0;
        while (stageIndex < stages.Count)
        {
            var stage = stages[stageIndex];
            var agent = agentsById[stage.AgentId];

            await EnsureNotCancelledAsync(ticket.Id, cancellationToken);

            stageRunCounts.TryGetValue(stage.Id, out var previousRuns);
            var attempt = previousRuns + 1;
            stageRunCounts[stage.Id] = attempt;

            var instructions = await GetInstructionsBlockAsync(agent.Id, cancellationToken);
            var requiresVerdict = stage.LoopBackToStageId is not null;
            var prompt = BuildStagePrompt(ticket, agent, previousOutput, instructions, requiresVerdict);

            string output;
            if (agent.Role == AgentRole.Coding)
            {
                var (codingOutput, commit) = await RunCodingStageAsync(ticket, project, agent, prompt, attempt, cancellationToken);
                output = codingOutput;
                steps.Add(new AgentWorkResultDto(ticket.Id, agent.Id, agent.Role, output, commit));
            }
            else
            {
                var response = await llmConnector.SendPromptAsync(new LlmRequest(prompt), cancellationToken);
                output = response.Content;
                steps.Add(new AgentWorkResultDto(ticket.Id, agent.Id, agent.Role, output, null));

                logger.LogInformation(
                    "Agent {AgentId} ({Role}) produced output for ticket {TicketId} in project {ProjectId}: {Output}",
                    agent.Id,
                    agent.Role,
                    ticket.Id,
                    ticket.ProjectId,
                    output);
            }

            previousOutput = output;

            if (requiresVerdict)
            {
                var passed = ParseVerdict(output);
                finalVerdictPassed = passed;
                finalVerdictAttempts = attempt;

                logger.LogInformation(
                    "Agent {AgentId} ({Role}) verdict for ticket {TicketId}, attempt {Attempt}: {Verdict}",
                    agent.Id,
                    agent.Role,
                    ticket.Id,
                    attempt,
                    passed ? "PASS" : "FAIL");

                if (!passed && attempt < stage.MaxLoopIterations!.Value)
                {
                    stageIndex = IndexOfStage(stages, stage.LoopBackToStageId!.Value);
                    continue;
                }
            }

            stageIndex++;
        }

        ticket.MoveToReview();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new TicketPipelineResultDto(TicketMappings.ToDto(ticket), steps, finalVerdictPassed, finalVerdictAttempts);
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
            ? $"{agent.Name} work for '{ticket.Title}'"
            : $"{agent.Name} work for '{ticket.Title}' (attempt {attempt})";
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

    private static string BuildStagePrompt(Ticket ticket, Agent agent, string? previousOutput, string? instructions, bool requiresVerdict)
    {
        var prompt = $"{AgentInstructionsFormatter.FormatInstructions(instructions)}You are {agent.Name} ({agent.Role}) working on ticket '{ticket.Title}'. Description: {ticket.Description}\n\n";

        if (previousOutput is not null)
        {
            prompt += $"Previous stage output:\n{previousOutput}\n\n";
        }

        prompt += "Carry out your part of the work on this ticket according to your role and instructions.";

        if (requiresVerdict)
        {
            prompt += " End your response with a line reading exactly 'RESULT: PASS' or 'RESULT: FAIL'.";
        }

        return prompt;
    }

    private static int IndexOfStage(IReadOnlyList<WorkflowStage> stages, Guid stageId)
    {
        for (var i = 0; i < stages.Count; i++)
        {
            if (stages[i].Id == stageId)
            {
                return i;
            }
        }

        throw new InvalidOperationException($"Loop-back target stage '{stageId}' was not found in the workflow.");
    }

    private static bool ParseVerdict(string output)
    {
        var matches = VerdictPattern.Matches(output);
        if (matches.Count == 0)
        {
            return true;
        }

        return string.Equals(matches[^1].Groups[1].Value, "PASS", StringComparison.OrdinalIgnoreCase);
    }
}
