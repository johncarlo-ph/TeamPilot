using Microsoft.EntityFrameworkCore;
using TeamPilot.Application.TicketAgentEvents;
using TeamPilot.Domain.Entities;
using TeamPilot.Infrastructure.Persistence;

namespace TeamPilot.Infrastructure.Repositories;

public class TicketAgentEventRepository(TeamPilotDbContext dbContext) : ITicketAgentEventRepository
{
    public async Task AddAsync(TicketAgentEvent agentEvent, CancellationToken cancellationToken = default) =>
        await dbContext.TicketAgentEvents.AddAsync(agentEvent, cancellationToken);

    public async Task<IReadOnlyList<TicketAgentEvent>> ListByTicketAsync(Guid ticketId, CancellationToken cancellationToken = default) =>
        await dbContext.TicketAgentEvents
            .AsNoTracking()
            .Where(e => e.TicketId == ticketId)
            .OrderBy(e => e.CreatedAtUtc)
            .ToListAsync(cancellationToken);
}
