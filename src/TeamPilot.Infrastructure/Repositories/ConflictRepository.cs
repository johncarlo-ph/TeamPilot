using Microsoft.EntityFrameworkCore;
using TeamPilot.Application.Conflicts;
using TeamPilot.Domain.Entities;
using TeamPilot.Infrastructure.Persistence;

namespace TeamPilot.Infrastructure.Repositories;

public class ConflictRepository(TeamPilotDbContext dbContext) : IConflictRepository
{
    public Task<Conflict?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Conflicts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Conflict>> ListByTicketAsync(Guid ticketId, CancellationToken cancellationToken = default) =>
        await dbContext.Conflicts
            .AsNoTracking()
            .Where(c => c.TicketId == ticketId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);
}
