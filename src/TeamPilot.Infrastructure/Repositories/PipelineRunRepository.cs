using Microsoft.EntityFrameworkCore;
using TeamPilot.Application.Pipelines;
using TeamPilot.Domain.Entities;
using TeamPilot.Infrastructure.Persistence;

namespace TeamPilot.Infrastructure.Repositories;

public class PipelineRunRepository(TeamPilotDbContext dbContext) : IPipelineRunRepository
{
    public Task<PipelineRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.PipelineRuns.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PipelineRun>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await dbContext.PipelineRuns
            .AsNoTracking()
            .Where(p => p.ProjectId == projectId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(PipelineRun pipelineRun, CancellationToken cancellationToken = default) =>
        await dbContext.PipelineRuns.AddAsync(pipelineRun, cancellationToken);
}
