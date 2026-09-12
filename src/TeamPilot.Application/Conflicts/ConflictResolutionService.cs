using FluentValidation;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Extensions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Conflicts.Dtos;
using TeamPilot.Application.Git;
using TeamPilot.Application.Llm;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Tickets;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Conflicts;

public sealed class ConflictResolutionService(
    ITicketRepository ticketRepository,
    IProjectRepository projectRepository,
    IConflictRepository conflictRepository,
    IGitService gitService,
    ILlmConnector llmConnector,
    IProjectAccessGuard projectAccessGuard,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork,
    IValidator<ResolveConflictManuallyRequest> resolveManuallyValidator,
    IValidator<AcceptAiSuggestionRequest> acceptAiSuggestionValidator) : IConflictResolutionService
{
    private const string DefaultTargetBranch = "main";

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

        var mergeCheck = await gitService.DetectMergeConflictsAsync(project.RepositoryPath, ticket.BranchName, DefaultTargetBranch, cancellationToken);

        var conflicts = mergeCheck.ConflictingFiles
            .Select(file => Conflict.Create(ticket.Id, file.FilePath, file.ConflictContent))
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

        var prompt = $"Suggest a resolution for the following merge conflict in file '{conflict.FilePath}':\n{conflict.ConflictingDiffContent}";
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

        conflict.ResolveManually(request.Note, request.ResolvedBy);

        await auditLogger.LogActionAsync(AuditEventType.ConflictResolvedManually, $"Conflict in '{conflict.FilePath}' resolved manually by {request.ResolvedBy}.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(conflict);
    }

    public async Task<ConflictDto> AcceptAiSuggestionAsync(Guid conflictId, AcceptAiSuggestionRequest request, CancellationToken cancellationToken = default)
    {
        await acceptAiSuggestionValidator.EnsureValidAsync(request, cancellationToken);

        var conflict = await GetConflictWithAccessAsync(conflictId, cancellationToken);

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
        conflict.ResolutionNote,
        conflict.Status,
        conflict.ResolvedAtUtc,
        conflict.ResolvedBy,
        conflict.CreatedAtUtc);
}
