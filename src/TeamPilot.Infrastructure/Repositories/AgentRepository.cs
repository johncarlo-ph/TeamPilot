using Microsoft.EntityFrameworkCore;
using TeamPilot.Application.Agents;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using TeamPilot.Infrastructure.Persistence;

namespace TeamPilot.Infrastructure.Repositories;

public class AgentRepository(TeamPilotDbContext dbContext) : IAgentRepository
{
    public Task<Agent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Agents
            .Include(a => a.Instructions)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Agent>> ListAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await dbContext.Agents
            .AsNoTracking()
            .Where(a => a.ProjectId == projectId)
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);

    public Task<Agent?> GetByProjectAndRoleAsync(Guid projectId, AgentRole role, CancellationToken cancellationToken = default) =>
        dbContext.Agents
            .Include(a => a.Instructions)
            .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Role == role && a.Status == AgentStatus.Active, cancellationToken);

    public async Task AddAsync(Agent agent, CancellationToken cancellationToken = default) =>
        await dbContext.Agents.AddAsync(agent, cancellationToken);
}
