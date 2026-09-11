using Microsoft.EntityFrameworkCore;
using TeamPilot.Application.Instructions;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using TeamPilot.Infrastructure.Persistence;

namespace TeamPilot.Infrastructure.Repositories;

public class InstructionRepository(TeamPilotDbContext dbContext) : IInstructionRepository
{
    public async Task<IReadOnlyList<Instruction>> ListByAgentAsync(Guid agentId, CancellationToken cancellationToken = default) =>
        await dbContext.Instructions
            .AsNoTracking()
            .Where(i => i.AgentId == agentId)
            .OrderBy(i => i.Type).ThenByDescending(i => i.Version)
            .ToListAsync(cancellationToken);

    public Task<Instruction?> GetCurrentAsync(Guid agentId, InstructionType type, CancellationToken cancellationToken = default) =>
        dbContext.Instructions
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.AgentId == agentId && i.Type == type && i.IsCurrent, cancellationToken);
}
