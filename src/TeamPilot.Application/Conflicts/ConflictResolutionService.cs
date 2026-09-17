using FluentValidation;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Extensions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Conflicts.Dtos;
using TeamPilot.Application.Git;
using TeamPilot.Application.Llm;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Sprints;
using TeamPilot.Application.Tickets;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Conflicts;

public sealed class ConflictResolutionService(
    ITicketRepository ticketRepository,
    IProjectRepository projectRepository,
    ISprintRepository sprintRepository,
    IConflictRepository conflictRepository,
    IGitService gitService,
    ILlmConnector llmConnector,
    IProjectAccessGuard projectAccessGuard,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork,
    IValidator<ResolveConflictManuallyRequest> resolveManuallyValidator,
    IValidator<AcceptAiSuggestionRequest> acceptAiSuggestionValidator) : IConflictResolutionService
{
    public async Task<IReadOnlyList<ConflictDto>> DetectConflictsAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        var ticket = await ticketRepository.GetByIdAsync(ticketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), ticketId);

        await projectAccessGuard.EnsureAccessAsync(ticket.ProjectId, cancellationToken);

        if (string.IsNullOrWhiteSpace(ticket.BranchName))
        {
            throw new InvalidOperationException("Ticket has no linked branch to check for conflicts.");
        }

        var project = await projectRepository.GetByIdAsync(ticket.ProjectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), ticket.ProjectId);

        // Guaranteed non-null: the BranchName check above already rejected a ticket with no
        // linked branch, and a branch only ever exists once a ticket has been assigned to a sprint.
        var sprint = await sprintRepository.GetByIdAsync(ticket.SprintId!.Value, cancellationToken)
            ?? throw new NotFoundException(nameof(Sprint), ticket.SprintId.Value);

        GitMergeConflictResult mergeCheck;
        await using (await GitRepositoryLock.AcquireAsync(project.RepositoryPath, cancellationToken))
        {
            mergeCheck = await gitService.DetectMergeConflictsAsync(project.RepositoryPath, ticket.BranchName, sprint.BaseBranch, cancellationToken);
        }

        var conflicts = mergeCheck.ConflictingFiles
            .Select(file => Conflict.Create(ticket.Id, file.FilePath, file.ConflictContent, mergeCheck.BaseTipSha))
            .ToList();

        foreach (var conflict in conflicts)
        {
            ticket.RaiseConflict(conflict);
        }

        if (conflicts.Count > 0)
        {
            await auditLogger.LogActionAsync(AuditEventType.ConflictsDetected, $"{conflicts.Count} conflict(s) detected on ticket '{ticket.Title}'.", cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return conflicts.Select(ToDto).ToList();
    }

    public async Task<ConflictDto> SuggestResolutionAsync(Guid conflictId, CancellationToken cancellationToken = default)
    {
        var conflict = await GetConflictWithAccessAsync(conflictId, cancellationToken);

        // ConflictingDiffContent is the whole file as it sits on disk mid-trial-merge, inline
        // conflict markers included - so the model has everything it needs (both sides' content)
        // to produce a complete replacement file, not just a prose description. This is required
        // for AcceptAiSuggestionAsync to be able to commit the result as-is.
        var prompt =
            $"The file '{conflict.FilePath}' has an unresolved Git merge conflict, shown below with " +
            "its inline conflict markers (<<<<<<<, =======, >>>>>>>). Resolve it and respond with " +
            "ONLY the complete, final content the file should have once resolved - no conflict " +
            "markers, no explanation, no markdown code fences, just the raw file content exactly as " +
            $"it should be written to disk:\n\n{conflict.ConflictingDiffContent}";

        var llmResponse = await llmConnector.SendPromptAsync(new LlmRequest(prompt), cancellationToken);

        conflict.RecordAiSuggestion(llmResponse.Content);

        await auditLogger.LogActionAsync(AuditEventType.ConflictResolutionSuggested, $"AI resolution suggested for conflict in '{conflict.FilePath}'.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(conflict);
    }

    public async Task<ConflictDto> ResolveManuallyAsync(Guid conflictId, ResolveConflictManuallyRequest request, CancellationToken cancellationToken = default)
    {
        await resolveManuallyValidator.EnsureValidAsync(request, cancellationToken);

        var conflict = await GetConflictWithAccessAsync(conflictId, cancellationToken);

        conflict.ResolveManually(request.ResolvedContent, request.Note, request.ResolvedBy);

        await auditLogger.LogActionAsync(AuditEventType.ConflictResolvedManually, $"Conflict in '{conflict.FilePath}' resolved manually by {request.ResolvedBy}.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(conflict);
    }

    public async Task<ConflictDto> AcceptAiSuggestionAsync(Guid conflictId, AcceptAiSuggestionRequest request, CancellationToken cancellationToken = default)
    {
        await acceptAiSuggestionValidator.EnsureValidAsync(request, cancellationToken);

        var conflict = await GetConflictWithAccessAsync(conflictId, cancellationToken);

        // Only records that this resolution is accepted (Conflict.ResolvedContent) - nothing is
        // written to Git here. ApprovalGateService.ApproveAsync is the one place that actually
        // applies every accepted resolution, as part of completing the real merge - see
        // docs/application.md for why deferring the write to that single, atomic step is what
        // makes this a real merge commit instead of a best-effort guess.
        conflict.AcceptAiSuggestion(request.ResolvedBy);

        await auditLogger.LogActionAsync(AuditEventType.ConflictAiSuggestionAccepted, $"AI suggestion accepted for conflict in '{conflict.FilePath}' by {request.ResolvedBy}.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(conflict);
    }

    public async Task<IReadOnlyList<ConflictDto>> ListByTicketAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        var ticket = await ticketRepository.GetByIdAsync(ticketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), ticketId);

        await projectAccessGuard.EnsureAccessAsync(ticket.ProjectId, cancellationToken);

        var conflicts = await conflictRepository.ListByTicketAsync(ticketId, cancellationToken);
        return conflicts.Select(ToDto).ToList();
    }

    private async Task<Conflict> GetConflictWithAccessAsync(Guid conflictId, CancellationToken cancellationToken)
    {
        var conflict = await conflictRepository.GetByIdAsync(conflictId, cancellationToken)
            ?? throw new NotFoundException(nameof(Conflict), conflictId);

        var ticket = await ticketRepository.GetByIdAsync(conflict.TicketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), conflict.TicketId);

        await projectAccessGuard.EnsureAccessAsync(ticket.ProjectId, cancellationToken);

        return conflict;
    }

    private static ConflictDto ToDto(Conflict conflict) => new(
        conflict.Id,
        conflict.TicketId,
        conflict.CommitId,
        conflict.FilePath,
        conflict.ConflictingDiffContent,
        conflict.AiSuggestedResolution,
        conflict.ResolvedContent,
        conflict.ResolutionNote,
        conflict.Status,
        conflict.ResolvedAtUtc,
        conflict.ResolvedBy,
        conflict.BaseTipSha,
        conflict.CreatedAtUtc);
}
