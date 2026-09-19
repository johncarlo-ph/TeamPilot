using TeamPilot.Domain.Entities;

namespace TeamPilot.Application.TicketProjectInstructions;

public interface ITicketProjectInstructionRepository
{
    Task AddAsync(TicketProjectInstruction instruction, CancellationToken cancellationToken = default);

    /// <summary>Read-only listing (<c>AsNoTracking</c>), oldest first, for a ticket's detail page.</summary>
    Task<IReadOnlyList<TicketProjectInstruction>> ListByTicketAsync(Guid ticketId, CancellationToken cancellationToken = default);
}
