using Microsoft.EntityFrameworkCore;
using TeamPilot.Application.Reviews;
using TeamPilot.Domain.Entities;
using TeamPilot.Infrastructure.Persistence;

namespace TeamPilot.Infrastructure.Repositories;

public class ReviewRepository(TeamPilotDbContext dbContext) : IReviewRepository
{
    public async Task<IReadOnlyList<Review>> ListByTicketAsync(Guid ticketId, CancellationToken cancellationToken = default) =>
        await dbContext.Reviews
            .AsNoTracking()
            .Where(r => r.TicketId == ticketId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);
}
