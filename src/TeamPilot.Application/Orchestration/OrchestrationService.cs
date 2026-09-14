using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
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
using TeamPilot.Application.TicketQuestions;
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
/// jumps back to its target stage, bounded by that stage's own <c>MaxLoopIterations</c>. If the
/// ticket's most recent review requested changes, that reviewer's comments are threaded into
/// every stage's prompt for this run (see <c>Approval.ApprovalGateService</c>, which re-runs the
/// pipeline immediately after such a review is submitted) - and each stage's own most recent
/// output for this ticket (see <see cref="IStageExecutionRepository"/>) is handed back to it, so
/// it can decide for itself whether the feedback actually changes anything for its part of the
/// work, rather than blindly redoing it.
///
/// Two things can pause a run instead of letting it finish: a stage can end its response with a
/// <c>QUESTION: ...</c> marker asking for human clarification, or a known operational failure
/// (<see cref="GitOperationException"/>/<see cref="LlmOperationException"/>) can occur. Either
/// way the ticket is <see cref="Ticket.Block"/>-ed, a <see cref="TicketQuestion"/> records what
/// happened, and the run returns early instead of reaching <c>ForReview</c>. Resuming (answering
/// the question, or retrying a failure - see <c>TicketQuestionService</c>) unblocks the ticket
/// and calls this method again, which threads the human's answer into the one stage that asked,
/// via the same "hand the stage its own prior output" mechanism used for review feedback.
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
    IStageExecutionRepository stageExecutionRepository,
    ITicketQuestionRepository ticketQuestionRepository,
    IProjectAccessGuard projectAccessGuard,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork,
    IBackgroundTaskRunner backgroundTaskRunner,
    ILogger<OrchestrationService> logger) : IOrchestrationService
{
    private static readonly Regex VerdictPattern =
        new(@"RESULT:\s*(PASS|FAIL)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ChangesPattern =
        new(@"CHANGES:\s*(NONE|MADE)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex QuestionPattern =
        new(@"QUESTION:\s*(.+)", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex FileBlockPattern =
        new(@"<file\s+path=[""'](?<path>[^""']+)[""']\s*>(?<content>.*?)</file>", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// The Coding stage's single prompt now includes a bounded snapshot of the ticket branch's own
    /// current files (see <see cref="AppendRepositorySnapshot"/>) plus a request for full per-file
    /// content back, so it needs far more headroom than every other stage's default 1024.
    /// </summary>
    private const int CodingStageMaxTokens = 8192;

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

        var steps = new List<AgentWorkResultDto>();

        // Tracked across the loop below so a caught failure (see the catch clause) can attribute
        // itself to whichever stage's agent was in flight - null if the failure happened before
        // any stage ran (e.g. linking the ticket's branch).
        Guid? currentAgentId = null;

        try
        {
            if (string.IsNullOrWhiteSpace(ticket.BranchName))
            {
                await LinkBranchAsync(ticket, project, cancellationToken);
            }

            await auditLogger.LogActionAsync(AuditEventType.TicketPipelineStarted, $"Pipeline started for ticket '{ticket.Title}'.", cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            // The most recent RequestChanges review's comments, if any - threaded into every stage's
            // prompt for this run (see BuildStagePrompt) so a re-run after a reviewer sends a ticket
            // back actually addresses what they flagged, rather than just repeating the same work.
            var reviewFeedback = ticket.Reviews
                .Where(r => r.Decision == ReviewDecision.RequestChanges && !string.IsNullOrWhiteSpace(r.Comments))
                .OrderByDescending(r => r.CreatedAtUtc)
                .FirstOrDefault()
                ?.Comments;

            // The most recently answered-but-not-yet-threaded question, if any - threaded only
            // into the one stage whose agent asked it (see BuildStagePrompt/questionContext
            // below), unlike reviewFeedback which goes into every stage's prompt.
            var answeredQuestion = await ticketQuestionRepository.GetMostRecentUnconsumedAnsweredAsync(ticket.Id, cancellationToken);

            // A snapshot of "what every agent said last time," taken once before this run adds any
            // new StageExecution rows - only fetched when there's feedback to react to at all.
            var priorOutputsByAgentId = reviewFeedback is not null || answeredQuestion is not null
                ? await stageExecutionRepository.GetLatestByTicketAsync(ticket.Id, cancellationToken)
                : new Dictionary<Guid, StageExecution>();

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
                currentAgentId = agent.Id;

                await EnsureNotCancelledAsync(ticket.Id, cancellationToken);

                stageRunCounts.TryGetValue(stage.Id, out var previousRuns);
                var attempt = previousRuns + 1;
                stageRunCounts[stage.Id] = attempt;

                // Only on a stage's first invocation this run, and only when this run was itself
                // triggered by review feedback or an answered question - a stage revisited via an
                // intra-run loop-back (attempt > 1) keeps using the rolling previousOutput instead,
                // so the feedback sources are never mixed. A stage with no prior execution (new to
                // this ticket, or newly added to the pipeline) has nothing to reaffirm against, so
                // it just does normal full-work prompting.
                string? priorOwnOutput = attempt == 1 && (reviewFeedback is not null || answeredQuestion is not null)
                    && priorOutputsByAgentId.TryGetValue(agent.Id, out var priorExecution)
                        ? priorExecution.Output
                        : null;

                // Unlike reviewFeedback (threaded into every stage), a question is specific to the
                // one stage/agent that asked it. Marked consumed the moment it's actually used in a
                // prompt, not gated on the rest of this run succeeding, since nothing else would
                // ever supersede it otherwise.
                string? questionContext = attempt == 1 && answeredQuestion is not null && answeredQuestion.AgentId == agent.Id
                    ? $"You previously asked \"{answeredQuestion.Prompt}\" and paused; the human answered: \"{answeredQuestion.AnswerText}\". Continue using this answer."
                    : null;

                if (questionContext is not null)
                {
                    answeredQuestion!.MarkConsumed();
                }

                var instructions = await GetInstructionsBlockAsync(agent.Id, cancellationToken);
                var requiresVerdict = stage.LoopBackToStageId is not null;
                var prompt = BuildStagePrompt(ticket, agent, previousOutput, instructions, requiresVerdict, reviewFeedback, priorOwnOutput, questionContext);

                string output;
                if (agent.Role == AgentRole.Coding)
                {
                    var (codingOutput, commit) = await RunCodingStageAsync(ticket, project, agent, prompt, attempt, checkForNoChanges: priorOwnOutput is not null, cancellationToken);
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

                var question = ParseQuestion(output);
                if (question is not null)
                {
                    return await BlockOnQuestionAsync(ticket, agent.Id, question, steps, cancellationToken);
                }

                // Persisted so a later review-triggered re-run can hand this stage's agent its own
                // prior output back (see priorOwnOutput above) - saved together with whatever this
                // iteration already changed (e.g. a new Commit), so it survives even if a later
                // stage in this same run throws.
                await stageExecutionRepository.AddAsync(StageExecution.Create(ticket.Id, agent.Id, output), cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);

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
        catch (Exception ex) when (ex is GitOperationException or LlmOperationException)
        {
            return await BlockOnFailureAsync(ticket, currentAgentId, steps, ex, cancellationToken);
        }
    }

    public async Task<TicketDto> StartPipelineAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        var ticket = await ticketRepository.GetByIdAsync(ticketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), ticketId);

        await projectAccessGuard.EnsureAccessAsync(ticket.ProjectId, cancellationToken);

        RunPipelineDetached(ticketId);

        return TicketMappings.ToDto(ticket);
    }

    public void RunPipelineDetached(Guid ticketId)
    {
        backgroundTaskRunner.Run(async (services, backgroundCancellationToken) =>
        {
            var backgroundOrchestrationService = services.GetRequiredService<IOrchestrationService>();
            try
            {
                await backgroundOrchestrationService.RunPipelineAsync(ticketId, backgroundCancellationToken);
            }
            catch (Exception ex)
            {
                await BlockOnBackgroundFailureAsync(services, ticketId, ex, backgroundCancellationToken);
            }
        });
    }

    /// <summary>
    /// Backstop for a detached pipeline run (see <see cref="RunPipelineDetached"/>): mirrors
    /// <see cref="BlockOnFailureAsync"/>'s own Git/LLM failure handling, but for whatever
    /// exception type made it out of <see cref="RunPipelineAsync"/> unhandled. Resolves every
    /// dependency from the background task's own scoped <paramref name="services"/> rather than
    /// this instance's fields, since by the time this runs the request that started it (and this
    /// instance's own scope) may be long gone.
    /// </summary>
    private static async Task BlockOnBackgroundFailureAsync(IServiceProvider services, Guid ticketId, Exception exception, CancellationToken cancellationToken)
    {
        var backgroundLogger = services.GetRequiredService<ILogger<OrchestrationService>>();
        backgroundLogger.LogError(exception, "Background pipeline run failed for ticket {TicketId}.", ticketId);

        var backgroundTicketRepository = services.GetRequiredService<ITicketRepository>();
        var ticket = await backgroundTicketRepository.GetByIdAsync(ticketId, cancellationToken);
        if (ticket is null || ticket.Status != TicketStatus.InProgress)
        {
            // RunPipelineAsync already left the ticket in a terminal state itself (e.g. Blocked
            // via its own Git/LLM handling, or Cancelled by a concurrent request) - nothing more
            // to do here.
            return;
        }

        var backgroundTicketQuestionRepository = services.GetRequiredService<ITicketQuestionRepository>();
        var question = TicketQuestion.CreateFailure(ticket.Id, agentId: null, exception.Message);
        await backgroundTicketQuestionRepository.AddAsync(question, cancellationToken);

        ticket.Block();

        var backgroundAuditLogger = services.GetRequiredService<IAuditLogger>();
        await backgroundAuditLogger.LogActionAsync(
            AuditEventType.TicketBlocked,
            $"Ticket '{ticket.Title}' blocked - background pipeline run failed: {exception.Message}",
            cancellationToken);

        var backgroundUnitOfWork = services.GetRequiredService<IUnitOfWork>();
        await backgroundUnitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Persists the clarifying question a stage asked, blocks the ticket, and returns an
    /// early-exit result instead of reaching <c>ForReview</c>.
    /// </summary>
    private async Task<TicketPipelineResultDto> BlockOnQuestionAsync(Ticket ticket, Guid agentId, string questionText, List<AgentWorkResultDto> steps, CancellationToken cancellationToken)
    {
        var question = TicketQuestion.CreateQuestion(ticket.Id, agentId, questionText);
        await ticketQuestionRepository.AddAsync(question, cancellationToken);

        ticket.Block();

        await auditLogger.LogActionAsync(AuditEventType.TicketBlocked, $"Ticket '{ticket.Title}' blocked - an agent asked a clarifying question.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new TicketPipelineResultDto(TicketMappings.ToDto(ticket), steps, TestingPassed: true, TestingAttempts: 0, Blocked: true, BlockingQuestionId: question.Id);
    }

    /// <summary>
    /// Persists a known operational failure (Git/LLM), blocks the ticket, and returns an
    /// early-exit result instead of letting the exception propagate to a raw 500 - see the
    /// user-facing "block only known operational failures" scope in
    /// <see cref="GitOperationException"/>/<see cref="LlmOperationException"/>'s doc comments;
    /// any other exception type still bubbles up unchanged.
    /// </summary>
    private async Task<TicketPipelineResultDto> BlockOnFailureAsync(Ticket ticket, Guid? agentId, List<AgentWorkResultDto> steps, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogWarning(exception, "Ticket {TicketId} blocked by an operational failure mid-pipeline.", ticket.Id);

        var question = TicketQuestion.CreateFailure(ticket.Id, agentId, exception.Message);
        await ticketQuestionRepository.AddAsync(question, cancellationToken);

        ticket.Block();

        await auditLogger.LogActionAsync(AuditEventType.TicketBlocked, $"Ticket '{ticket.Title}' blocked - {exception.Message}", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new TicketPipelineResultDto(TicketMappings.ToDto(ticket), steps, TestingPassed: true, TestingAttempts: 0, Blocked: true, BlockingQuestionId: question.Id);
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

    private async Task<(string LlmOutput, CommitDto? Commit)> RunCodingStageAsync(
        Ticket ticket,
        Project project,
        Agent agent,
        string prompt,
        int attempt,
        bool checkForNoChanges,
        CancellationToken cancellationToken)
    {
        // Gathered by plain Git/filesystem reads (no LLM call), so the agent edits against the
        // ticket branch's real current files while still costing exactly one Claude API call for
        // this stage invocation. Locked on its own, released before the (slow) LLM call below,
        // rather than held for the whole method - see GitRepositoryLock.
        IReadOnlyDictionary<string, string> snapshot;
        await using (await GitRepositoryLock.AcquireAsync(project.RepositoryPath, cancellationToken))
        {
            snapshot = await gitService.GetRepositorySnapshotAsync(project.RepositoryPath, ticket.BranchName!, cancellationToken);
        }

        var promptWithContext = AppendRepositorySnapshot(prompt, snapshot);

        var response = await llmConnector.SendPromptAsync(new LlmRequest(promptWithContext, CodingStageMaxTokens), cancellationToken);

        // A clarifying question pre-empts everything else - no commit/push, regardless of
        // whether this is a reaffirm-style invocation. The outer loop's own ParseQuestion check
        // (on this same response.Content) is what actually blocks the ticket.
        if (ParseQuestion(response.Content) is not null)
        {
            return (response.Content, null);
        }

        // Only asked of - and only honored for - a Coding stage reaffirming its own prior output
        // (see priorOwnOutput/BuildStagePrompt): a "no changes needed" response must not still
        // produce a no-op commit. A missing or unparseable marker falls through and commits as
        // usual, matching every other Coding invocation.
        if (checkForNoChanges && ParseChanges(response.Content) == false)
        {
            return (response.Content, null);
        }

        var fileChanges = ParseFileChanges(response.Content);
        if (fileChanges.Count == 0)
        {
            // Unlike the explicit CHANGES: NONE marker above, this is an unformatted response -
            // still recorded as a StageExecution by the caller (nothing is lost), just never
            // git-committed. Logged since it's unexpected rather than a deliberate "no changes".
            logger.LogWarning(
                "Coding agent {AgentId} produced no parseable <file> blocks for ticket {TicketId}; nothing committed.",
                agent.Id,
                ticket.Id);
            return (response.Content, null);
        }

        var message = attempt == 1
            ? $"{agent.Name} work for '{ticket.Title}'"
            : $"{agent.Name} work for '{ticket.Title}' (attempt {attempt})";

        var filesToCommit = new Dictionary<string, string>();
        foreach (var (path, content) in fileChanges)
        {
            filesToCommit[path] = content;
        }

        await using (await GitRepositoryLock.AcquireAsync(project.RepositoryPath, cancellationToken))
        {
            // Re-checked fresh here, holding the same lock TicketService.CancelAsync holds while
            // persisting Cancelled and deleting a cancelled ticket's branch. Whichever of the two
            // acquires the lock first fully finishes before the other proceeds, so a
            // cancellation that lands while this stage's LLM call was already in flight (the one
            // gap that's cooperative, not preemptive - the LLM call itself can't be aborted) can
            // never be resurrected by this commit/push landing afterward, and this commit/push
            // can never run after a cancellation whose deletion already completed. See
            // docs/application.md.
            var currentStatus = await ticketRepository.GetStatusAsync(ticket.Id, cancellationToken);
            if (currentStatus == TicketStatus.Cancelled)
            {
                return (response.Content, null);
            }

            var commitResult = await gitService.CommitFilesAsync(
                project.RepositoryPath,
                ticket.BranchName!,
                filesToCommit,
                message,
                agent.Name,
                cancellationToken);

            var accessToken = credentialProtector.Unprotect(project.EncryptedAccessToken);
            await gitService.PushAsync(project.RepositoryPath, ticket.BranchName!, accessToken, cancellationToken);

            var commit = Commit.Create(ticket.Id, ticket.BranchName!, commitResult.CommitHash, message, commitResult.DiffContent, agent.Id);
            ticket.AddCommit(commit);
            // Not saved here - the caller persists this together with the StageExecution it records
            // for every stage invocation, Coding included, in one call.

            var commitDto = new CommitDto(commit.Id, commit.TicketId, commit.BranchName, commit.CommitHash, commit.Message, commit.DiffContent, commit.AuthorAgentId, commit.CreatedAtUtc);

            return (response.Content, commitDto);
        }
    }

    private async Task LinkBranchAsync(Ticket ticket, Project project, CancellationToken cancellationToken)
    {
        var branchName = GenerateBranchName(ticket);
        var accessToken = credentialProtector.Unprotect(project.EncryptedAccessToken);

        await using (await GitRepositoryLock.AcquireAsync(project.RepositoryPath, cancellationToken))
        {
            // Re-checked fresh, under the same lock: closes the window where a concurrent
            // cancellation - one with no branch yet to delete, so it wouldn't otherwise touch
            // Git at all - could land while this initial link is in flight, leaving a Cancelled
            // ticket pointing at a brand-new branch nobody ever deletes.
            await EnsureNotCancelledAsync(ticket.Id, cancellationToken);

            // Fetch first so the new branch is cut from the remote's current tip of the base
            // branch, not a possibly-stale local one.
            await gitService.FetchAsync(project.RepositoryPath, accessToken, cancellationToken);
            await gitService.EnsureBranchAsync(project.RepositoryPath, branchName, project.BaseBranch, cancellationToken);
            await gitService.PushAsync(project.RepositoryPath, branchName, accessToken, cancellationToken);
        }

        ticket.LinkBranch(branchName);
    }

    // The ticket's Id is the only unique "number" a ticket has today (no sequential per-project
    // numbering exists) - the branch is named after it directly, with no title slug or prefix.
    private static string GenerateBranchName(Ticket ticket) => ticket.Id.ToString("N")[..8];

    private static string BuildStagePrompt(
        Ticket ticket,
        Agent agent,
        string? previousOutput,
        string? instructions,
        bool requiresVerdict,
        string? reviewFeedback,
        string? priorOwnOutput,
        string? questionContext)
    {
        var prompt = AgentInstructionsFormatter.FormatInstructions(instructions);

        if (!string.IsNullOrWhiteSpace(reviewFeedback))
        {
            prompt += $"A human reviewer sent this ticket back with the following feedback - address it:\n{reviewFeedback}\n\n";
        }

        if (!string.IsNullOrWhiteSpace(questionContext))
        {
            prompt += $"{questionContext}\n\n";
        }

        prompt += $"You are {agent.Name} ({agent.Role}) working on ticket '{ticket.Title}'. Description: {ticket.Description}\n\n";

        if (previousOutput is not null)
        {
            prompt += $"Previous stage output:\n{previousOutput}\n\n";
        }

        if (priorOwnOutput is not null)
        {
            prompt += $"Your own previous output for this ticket was:\n{priorOwnOutput}\n\n" +
                "If the reviewer's feedback above doesn't affect this, briefly reaffirm your previous conclusion instead of redoing the work. Otherwise, revise it to address the feedback.";
        }
        else
        {
            prompt += "Carry out your part of the work on this ticket according to your role and instructions.";
        }

        if (requiresVerdict)
        {
            prompt += " End your response with a line reading exactly 'RESULT: PASS' or 'RESULT: FAIL'.";
        }

        if (agent.Role == AgentRole.Coding)
        {
            if (priorOwnOutput is not null)
            {
                prompt += " Also end your response with a line reading exactly 'CHANGES: NONE' if you made no code changes, or 'CHANGES: MADE' if you did.";
            }

            prompt += " For every file you create or modify, include its complete new content (the whole " +
                "file, not a diff) in its own block formatted exactly as <file path=\"relative/path/from/repo/root\">" +
                "...entire file content...</file> - one block per file, with no other text inside the tags.";
        }

        prompt += " If you need clarification from a human before you can continue, respond with ONLY a single line reading 'QUESTION: <your question>' and nothing else.";

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

    /// <summary>
    /// Parses a 'CHANGES: NONE'/'CHANGES: MADE' marker - <see langword="false"/> only when the
    /// last such marker in the output is unambiguously 'NONE'; a missing or unparseable marker
    /// returns <see langword="null"/>, which <see cref="RunCodingStageAsync"/> treats the same as
    /// 'MADE' (commit as usual) rather than silently dropping a real change.
    /// </summary>
    private static bool? ParseChanges(string output)
    {
        var matches = ChangesPattern.Matches(output);
        if (matches.Count == 0)
        {
            return null;
        }

        return !string.Equals(matches[^1].Groups[1].Value, "NONE", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses a 'QUESTION: &lt;text&gt;' marker - everything after the marker (to the end of the
    /// output) is taken as the question, since unlike RESULT/CHANGES it's free text rather than a
    /// fixed set of words. <see langword="null"/> when no such marker is present.
    /// </summary>
    private static string? ParseQuestion(string output)
    {
        var match = QuestionPattern.Match(output);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    /// <summary>
    /// Parses every <c>&lt;file path="..."&gt;...&lt;/file&gt;</c> block out of a Coding stage's raw
    /// response (see the output contract appended in <see cref="BuildStagePrompt"/>). A path that
    /// appears more than once keeps only its last occurrence's content, matching the model's final
    /// intent. Not unit-tested directly, same as <see cref="ParseVerdict"/>/<see cref="ParseChanges"/>/
    /// <see cref="ParseQuestion"/> - covered indirectly through <c>OrchestrationServiceTests</c>.
    /// </summary>
    private static IReadOnlyList<(string Path, string Content)> ParseFileChanges(string output)
    {
        var results = new List<(string Path, string Content)>();

        foreach (Match match in FileBlockPattern.Matches(output))
        {
            var path = match.Groups["path"].Value.Trim();
            if (path.Length == 0)
            {
                continue;
            }

            results.Add((path, StripSurroundingNewline(match.Groups["content"].Value)));
        }

        return results;
    }

    /// <summary>Strips exactly one leading and one trailing newline the model typically puts right
    /// after the opening &lt;file&gt; tag / before the closing one - not a full <c>Trim()</c>, which
    /// would also eat intentional blank lines at the start/end of the file's real content.</summary>
    private static string StripSurroundingNewline(string content)
    {
        if (content.StartsWith("\r\n", StringComparison.Ordinal))
        {
            content = content[2..];
        }
        else if (content.StartsWith('\n'))
        {
            content = content[1..];
        }

        if (content.EndsWith("\r\n", StringComparison.Ordinal))
        {
            content = content[..^2];
        }
        else if (content.EndsWith('\n'))
        {
            content = content[..^1];
        }

        return content;
    }

    /// <summary>
    /// Renders the Coding stage's repository snapshot (see <see cref="IGitService.GetRepositorySnapshotAsync"/>)
    /// as a labeled context block appended to its prompt - plain string building, not an LLM call, so
    /// the Coding stage still costs exactly one Claude API call per invocation. An empty snapshot (e.g.
    /// a brand-new repository) adds nothing.
    /// </summary>
    private static string AppendRepositorySnapshot(string prompt, IReadOnlyDictionary<string, string> snapshot)
    {
        if (snapshot.Count == 0)
        {
            return prompt;
        }

        var builder = new StringBuilder(prompt);
        builder.Append("\n\nCurrent contents of files already in this ticket's branch:\n\n");

        foreach (var (path, content) in snapshot)
        {
            builder.Append("### ").Append(path).Append('\n');
            builder.Append("```\n").Append(content).Append("\n```\n\n");
        }

        return builder.ToString();
    }
}
