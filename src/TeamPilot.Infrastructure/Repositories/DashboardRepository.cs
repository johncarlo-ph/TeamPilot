using Microsoft.EntityFrameworkCore;
using TeamPilot.Application.Dashboard;
using TeamPilot.Application.Dashboard.Rows;
using TeamPilot.Domain.Enums;
using TeamPilot.Infrastructure.Persistence;

namespace TeamPilot.Infrastructure.Repositories;

public class DashboardRepository(TeamPilotDbContext dbContext) : IDashboardRepository
{
    private static readonly TicketStatus[] UnfinishedStatuses =
        [TicketStatus.InProgress, TicketStatus.ForReview, TicketStatus.Blocked];

    public async Task<int> CountActiveSprintsAsync(DateTime asOfUtc, IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default) =>
        await dbContext.Sprints
            .AsNoTracking()
            .CountAsync(s =>
                !s.IsRemoved &&
                projectIds.Contains(s.ProjectId) &&
                s.SprintStartDate != null &&
                s.SprintEndDate != null &&
                s.SprintStartDate <= asOfUtc &&
                s.SprintEndDate >= asOfUtc,
                cancellationToken);

    public async Task<IReadOnlyDictionary<TicketStatus, int>> GetGlobalTicketStatusCountsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default)
    {
        var counts = await dbContext.Tickets
            .AsNoTracking()
            .Where(t => projectIds.Contains(t.ProjectId) && t.Status != TicketStatus.Cancelled)
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(c => c.Status, c => c.Count);
    }

    public async Task<IReadOnlyList<AttentionTicketRow>> GetAttentionTicketsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default)
    {
        var statuses = new[] { TicketStatus.Blocked, TicketStatus.ForReview };

        return await (
            from t in dbContext.Tickets.AsNoTracking()
            where projectIds.Contains(t.ProjectId) && statuses.Contains(t.Status)
            join p in dbContext.Projects.AsNoTracking() on t.ProjectId equals p.Id
            join s in dbContext.Sprints.AsNoTracking() on t.SprintId equals (Guid?)s.Id into sprintJoin
            from s in sprintJoin.DefaultIfEmpty()
            orderby t.CreatedAtUtc descending
            select new AttentionTicketRow(t.Id, t.Title, t.Status, t.ProjectId, p.Name, t.SprintId, s == null ? null : s.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AtRiskSprintRow>> GetAtRiskSprintsAsync(DateTime thresholdUtc, IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default)
    {
        var unfinishedCounts = dbContext.Tickets
            .AsNoTracking()
            .Where(t => t.SprintId != null && UnfinishedStatuses.Contains(t.Status))
            .GroupBy(t => t.SprintId!.Value)
            .Select(g => new { SprintId = g.Key, Count = g.Count() });

        return await (
            from s in dbContext.Sprints.AsNoTracking()
            where !s.IsRemoved && projectIds.Contains(s.ProjectId) && s.SprintEndDate != null && s.SprintEndDate <= thresholdUtc
            join p in dbContext.Projects.AsNoTracking() on s.ProjectId equals p.Id
            join uc in unfinishedCounts on s.Id equals uc.SprintId
            orderby s.SprintEndDate
            select new AtRiskSprintRow(s.Id, s.Name, s.ProjectId, p.Name, s.SprintEndDate!.Value, uc.Count))
            .ToListAsync(cancellationToken);
    }
}
