using TeamPilot.Domain.Entities;

namespace TeamPilot.Application.TicketAgentEvents;

public interface ITicketAgentEventRepository
{
    Task AddAsync(TicketAgentEvent agentEvent, CancellationToken cancellationToken = default);

    /// <summary>Read-only listing (<c>AsNoTracking</c>), oldest first, for a ticket's full agent
    /// event log.</summary>
    Task<IReadOnlyList<TicketAgentEvent>> ListByTicketAsync(Guid ticketId, CancellationToken cancellationToken = default);
}
