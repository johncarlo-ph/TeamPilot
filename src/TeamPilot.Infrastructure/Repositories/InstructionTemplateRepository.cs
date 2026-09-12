using Microsoft.EntityFrameworkCore;
using TeamPilot.Application.InstructionTemplates;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using TeamPilot.Infrastructure.Persistence;

namespace TeamPilot.Infrastructure.Repositories;

public class InstructionTemplateRepository(TeamPilotDbContext dbContext) : IInstructionTemplateRepository
{
    public Task<InstructionTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.InstructionTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<InstructionTemplate>> ListAsync(AgentRole? role, InstructionType? type, CancellationToken cancellationToken = default)
    {
        var query = dbContext.InstructionTemplates.AsNoTracking().AsQueryable();

        if (role is not null)
        {
            query = query.Where(t => t.Role == role);
        }

        if (type is not null)
        {
            query = query.Where(t => t.Type == type);
        }

        return await query.OrderBy(t => t.Name).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(InstructionTemplate template, CancellationToken cancellationToken = default) =>
        await dbContext.InstructionTemplates.AddAsync(template, cancellationToken);

    public Task DeleteAsync(InstructionTemplate template, CancellationToken cancellationToken = default)
    {
        dbContext.InstructionTemplates.Remove(template);
        return Task.CompletedTask;
    }
}
