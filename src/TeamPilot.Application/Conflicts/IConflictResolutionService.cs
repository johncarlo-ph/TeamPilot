using TeamPilot.Application.Conflicts.Dtos;

namespace TeamPilot.Application.Conflicts;

public interface IConflictResolutionService
{
    /// <summary>
    /// Checks the ticket's branch for merge conflicts against the target branch via Git and
    /// persists any found as <c>Conflict</c> records on the ticket.
    /// </summary>
    Task<IReadOnlyList<ConflictDto>> DetectConflictsAsync(Guid ticketId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the configured LLM to resolve a detected conflict, storing the complete resolved
    /// file content it returns (not a prose description) as <c>Conflict.AiSuggestedResolution</c>
    /// - ready to be written to disk as-is if accepted.
    /// </summary>
    Task<ConflictDto> SuggestResolutionAsync(Guid conflictId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a human-authored resolution - the complete file content to use, plus an optional
    /// short note - without touching Git. Nothing is written to the repository until
    /// <c>ApprovalGateService.ApproveAsync</c> applies every accepted resolution as part of the
    /// real merge - see docs/application.md.
    /// </summary>
    Task<ConflictDto> ResolveManuallyAsync(Guid conflictId, ResolveConflictManuallyRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Accepts the AI-suggested resolution (copies it into <c>Conflict.ResolvedContent</c>)
    /// without touching Git. Nothing is written to the repository until
    /// <c>ApprovalGateService.ApproveAsync</c> applies every accepted resolution as part of the
    /// real merge - see docs/application.md.
    /// </summary>
    Task<ConflictDto> AcceptAiSuggestionAsync(Guid conflictId, AcceptAiSuggestionRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConflictDto>> ListByTicketAsync(Guid ticketId, CancellationToken cancellationToken = default);
}
