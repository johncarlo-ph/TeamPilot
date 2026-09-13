using TeamPilot.Domain.Entities;

namespace TeamPilot.Application.TicketQuestions;

public interface ITicketQuestionRepository
{
    Task AddAsync(TicketQuestion question, CancellationToken cancellationToken = default);

    /// <summary>Tracked - callers mutate the returned instance (e.g. <c>Answer</c>).</summary>
    Task<TicketQuestion?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Read-only listing (<c>AsNoTracking</c>), oldest first, for a ticket's full Q&amp;A history.</summary>
    Task<IReadOnlyList<TicketQuestion>> ListByTicketAsync(Guid ticketId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tracked. The single most recent <see cref="TicketQuestion"/> for this ticket - since
    /// <see cref="Ticket.Block"/> and the record that caused it are always created in the same
    /// call, this is always the exact reason the ticket is currently blocked. Used by
    /// <c>TicketQuestionService.RetryAsync</c>.
    /// </summary>
    Task<TicketQuestion?> GetMostRecentAsync(Guid ticketId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tracked. The most recently answered <see cref="TicketQuestion"/> for this ticket that
    /// hasn't yet been threaded into a resumed pipeline run (<c>Consumed == false</c>) - used by
    /// <see cref="Orchestration.OrchestrationService"/> to hand the one stage that asked it the
    /// human's answer, exactly once.
    /// </summary>
    Task<TicketQuestion?> GetMostRecentUnconsumedAnsweredAsync(Guid ticketId, CancellationToken cancellationToken = default);
}
