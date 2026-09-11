using Microsoft.EntityFrameworkCore;
using TeamPilot.Application.Commits;
using TeamPilot.Domain.Entities;
using TeamPilot.Infrastructure.Persistence;

namespace TeamPilot.Infrastructure.Repositories;

public class CommitRepository(TeamPilotDbContext dbContext) : ICommitRepository
{
    public async Task<IReadOnlyList<Commit>> ListByTicketAsync(Guid ticketId, CancellationToken cancellationToken = default) =>
        await dbContext.Commits
            .AsNoTracking()
            .Where(c => c.TicketId == ticketId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);
}
