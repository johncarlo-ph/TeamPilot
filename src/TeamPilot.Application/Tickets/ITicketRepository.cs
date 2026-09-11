using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Tickets;

public interface ITicketRepository
{
    /// <summary>
    /// Loads a ticket tracked for updates, with its assignments, commits, reviews, and
    /// conflicts included so aggregate behavior methods can be called safely.
    /// </summary>
    Task<Ticket?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lean, read-only listing of a project's ticket board.
    /// </summary>
    Task<IReadOnlyList<Ticket>> ListAsync(Guid projectId, TicketStatus? status, CancellationToken cancellationToken = default);

    Task AddAsync(Ticket ticket, CancellationToken cancellationToken = default);
}
