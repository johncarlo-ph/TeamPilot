using Microsoft.EntityFrameworkCore;
using TeamPilot.Application.Orchestration;
using TeamPilot.Domain.Entities;
using TeamPilot.Infrastructure.Persistence;

namespace TeamPilot.Infrastructure.Repositories;

public class StageExecutionRepository(TeamPilotDbContext dbContext) : IStageExecutionRepository
{
    public async Task AddAsync(StageExecution execution, CancellationToken cancellationToken = default) =>
        await dbContext.StageExecutions.AddAsync(execution, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, StageExecution>> GetLatestByTicketAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        // Grouped client-side rather than as a GroupBy().Select(g => g.OrderBy...First())
        // query - EF Core's SQL Server translation of "latest row per group" is unreliable across
        // versions, and a single ticket's total execution count (stages x runs x loop attempts)
        // is small enough that fetching them all is not a real cost.
        var executions = await dbContext.StageExecutions
            .AsNoTracking()
            .Where(e => e.TicketId == ticketId)
            .ToListAsync(cancellationToken);

        return executions
            .GroupBy(e => e.AgentId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.CreatedAtUtc).First());
    }
}
