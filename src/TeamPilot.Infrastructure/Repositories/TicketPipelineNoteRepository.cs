using Microsoft.EntityFrameworkCore;
using TeamPilot.Application.TicketPipelineNotes;
using TeamPilot.Domain.Entities;
using TeamPilot.Infrastructure.Persistence;

namespace TeamPilot.Infrastructure.Repositories;

public class TicketPipelineNoteRepository(TeamPilotDbContext dbContext) : ITicketPipelineNoteRepository
{
    public async Task AddAsync(TicketPipelineNote note, CancellationToken cancellationToken = default) =>
        await dbContext.TicketPipelineNotes.AddAsync(note, cancellationToken);

    public async Task<IReadOnlyList<TicketPipelineNote>> ListByTicketAsync(Guid ticketId, CancellationToken cancellationToken = default) =>
        await dbContext.TicketPipelineNotes
            .AsNoTracking()
            .Where(n => n.TicketId == ticketId)
            .OrderBy(n => n.CreatedAtUtc)
            .ToListAsync(cancellationToken);
}
