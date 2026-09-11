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
    /// Asks the configured LLM to suggest a resolution for a detected conflict.
    /// </summary>
    Task<ConflictDto> SuggestResolutionAsync(Guid conflictId, CancellationToken cancellationToken = default);

    Task<ConflictDto> ResolveManuallyAsync(Guid conflictId, ResolveConflictManuallyRequest request, CancellationToken cancellationToken = default);

    Task<ConflictDto> AcceptAiSuggestionAsync(Guid conflictId, AcceptAiSuggestionRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConflictDto>> ListByTicketAsync(Guid ticketId, CancellationToken cancellationToken = default);
}
