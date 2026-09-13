using Microsoft.EntityFrameworkCore;
using TeamPilot.Application.Tickets;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using TeamPilot.Infrastructure.Persistence;

namespace TeamPilot.Infrastructure.Repositories;

public class TicketRepository(TeamPilotDbContext dbContext) : ITicketRepository
{
    public Task<Ticket?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Tickets
            .Include(t => t.Assignments)
            .Include(t => t.Commits)
            .Include(t => t.Reviews)
            .Include(t => t.Conflicts)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Ticket>> ListAsync(Guid projectId, TicketStatus? status, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Tickets.AsNoTracking().Where(t => t.ProjectId == projectId);

        if (status is not null)
        {
            query = query.Where(t => t.Status == status);
        }

        return await query.OrderByDescending(t => t.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Ticket ticket, CancellationToken cancellationToken = default) =>
        await dbContext.Tickets.AddAsync(ticket, cancellationToken);

    public async Task<TicketStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken = default) =>
        await dbContext.Tickets
            .AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => (TicketStatus?)t.Status)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Ticket?> GetByBranchNameAsync(Guid projectId, string branchName, CancellationToken cancellationToken = default) =>
        await dbContext.Tickets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.ProjectId == projectId && t.BranchName == branchName, cancellationToken);

    public async Task<int> CountByStatusAsync(Guid projectId, TicketStatus status, CancellationToken cancellationToken = default) =>
        await dbContext.Tickets
            .AsNoTracking()
            .CountAsync(t => t.ProjectId == projectId && t.Status == status, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyDictionary<TicketStatus, int>>> GetStatusCountsByProjectAsync(
        IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default)
    {
        var counts = await dbContext.Tickets
            .AsNoTracking()
            .Where(t => projectIds.Contains(t.ProjectId))
            .GroupBy(t => new { t.ProjectId, t.Status })
            .Select(g => new { g.Key.ProjectId, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return counts
            .GroupBy(c => c.ProjectId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<TicketStatus, int>)g.ToDictionary(c => c.Status, c => c.Count));
    }
}
