using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Tickets;
using TeamPilot.Domain.Entities;

namespace TeamPilot.Application.TicketAgentEvents;

public sealed class TicketAgentEventService(
    ITicketRepository ticketRepository,
    ITicketAgentEventRepository ticketAgentEventRepository,
    IProjectAccessGuard projectAccessGuard) : ITicketAgentEventService
{
    public async Task<IReadOnlyList<TicketAgentEvent>> ListByTicketAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        var ticket = await ticketRepository.GetByIdAsync(ticketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), ticketId);

        await projectAccessGuard.EnsureAccessAsync(ticket.ProjectId, cancellationToken);

        return await ticketAgentEventRepository.ListByTicketAsync(ticket.Id, cancellationToken);
    }
}
