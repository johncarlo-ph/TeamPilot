using Microsoft.EntityFrameworkCore;
using TeamPilot.Application.Projects;
using TeamPilot.Domain.Entities;
using TeamPilot.Infrastructure.Persistence;

namespace TeamPilot.Infrastructure.Repositories;

public class ProjectRepository(TeamPilotDbContext dbContext) : IProjectRepository
{
    public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Projects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Projects.AsNoTracking().OrderBy(p => p.Name).ToListAsync(cancellationToken);

    public async Task AddAsync(Project project, CancellationToken cancellationToken = default) =>
        await dbContext.Projects.AddAsync(project, cancellationToken);

    public async Task<IReadOnlyList<Project>> SearchAsync(string query, IReadOnlyCollection<Guid>? allowedProjectIds, int maxResults, CancellationToken cancellationToken = default)
    {
        var nameQuery = dbContext.Projects.AsNoTracking().Where(p => !p.IsRemoved && EF.Functions.Like(p.Name, $"%{query}%"));

        if (allowedProjectIds is not null)
        {
            nameQuery = nameQuery.Where(p => allowedProjectIds.Contains(p.Id));
        }

        return await nameQuery.OrderBy(p => p.Name).Take(maxResults).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Project>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        await dbContext.Projects.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
}
