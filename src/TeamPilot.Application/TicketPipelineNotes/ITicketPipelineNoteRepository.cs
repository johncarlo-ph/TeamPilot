using TeamPilot.Domain.Entities;

namespace TeamPilot.Application.TicketPipelineNotes;

public interface ITicketPipelineNoteRepository
{
    Task AddAsync(TicketPipelineNote note, CancellationToken cancellationToken = default);

    /// <summary>Read-only listing (<c>AsNoTracking</c>), oldest first, for a ticket's detail page.</summary>
    Task<IReadOnlyList<TicketPipelineNote>> ListByTicketAsync(Guid ticketId, CancellationToken cancellationToken = default);
}
