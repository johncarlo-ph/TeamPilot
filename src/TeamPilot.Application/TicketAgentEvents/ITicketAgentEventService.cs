using TeamPilot.Domain.Entities;

namespace TeamPilot.Application.TicketAgentEvents;

public interface ITicketAgentEventService
{
    /// <summary>
    /// Lists a ticket's full agent event log (oldest first), after checking the caller has access
    /// to the ticket's project - see <c>TicketAgentEventsController</c>, whose route carries only
    /// a <c>ticketId</c>, not a <c>projectId</c>, to check against directly.
    /// </summary>
    Task<IReadOnlyList<TicketAgentEvent>> ListByTicketAsync(Guid ticketId, CancellationToken cancellationToken = default);
}
