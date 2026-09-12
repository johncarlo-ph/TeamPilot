using Microsoft.EntityFrameworkCore;
using TeamPilot.Application.Workflow;
using TeamPilot.Domain.Entities;
using TeamPilot.Infrastructure.Persistence;

namespace TeamPilot.Infrastructure.Repositories;

public class WorkflowStageRepository(TeamPilotDbContext dbContext) : IWorkflowStageRepository
{
    public Task<WorkflowStage?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.WorkflowStages.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<WorkflowStage>> ListOrderedAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await dbContext.WorkflowStages
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.Order)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(WorkflowStage stage, CancellationToken cancellationToken = default) =>
        await dbContext.WorkflowStages.AddAsync(stage, cancellationToken);

    public void Remove(WorkflowStage stage) => dbContext.WorkflowStages.Remove(stage);
}
