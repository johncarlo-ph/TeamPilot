using Microsoft.EntityFrameworkCore;
using TeamPilot.Application.TicketProjectInstructions;
using TeamPilot.Domain.Entities;
using TeamPilot.Infrastructure.Persistence;

namespace TeamPilot.Infrastructure.Repositories;

public class TicketProjectInstructionRepository(TeamPilotDbContext dbContext) : ITicketProjectInstructionRepository
{
    public async Task AddAsync(TicketProjectInstruction instruction, CancellationToken cancellationToken = default) =>
        await dbContext.TicketProjectInstructions.AddAsync(instruction, cancellationToken);

    public async Task<IReadOnlyList<TicketProjectInstruction>> ListByTicketAsync(Guid ticketId, CancellationToken cancellationToken = default) =>
        await dbContext.TicketProjectInstructions
            .AsNoTracking()
            .Where(i => i.TicketId == ticketId)
            .OrderBy(i => i.CreatedAtUtc)
            .ToListAsync(cancellationToken);
}
