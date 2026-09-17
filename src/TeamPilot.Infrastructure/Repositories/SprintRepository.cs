using Microsoft.EntityFrameworkCore;
using TeamPilot.Application.Sprints;
using TeamPilot.Domain.Entities;
using TeamPilot.Infrastructure.Persistence;

namespace TeamPilot.Infrastructure.Repositories;

public class SprintRepository(TeamPilotDbContext dbContext) : ISprintRepository
{
    public Task<Sprint?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Sprints.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Sprint>> ListAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await dbContext.Sprints.AsNoTracking().Where(s => s.ProjectId == projectId).OrderBy(s => s.Name).ToListAsync(cancellationToken);

    public async Task AddAsync(Sprint sprint, CancellationToken cancellationToken = default) =>
        await dbContext.Sprints.AddAsync(sprint, cancellationToken);
}
