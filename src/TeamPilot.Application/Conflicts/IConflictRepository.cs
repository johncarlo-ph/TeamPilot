using TeamPilot.Domain.Entities;

namespace TeamPilot.Application.Conflicts;

public interface IConflictRepository
{
    /// <summary>
    /// Loads a single conflict tracked for updates - conflicts are addressed directly by their
    /// own id (not nested under a ticket route) once detected.
    /// </summary>
    Task<Conflict?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Conflict>> ListByTicketAsync(Guid ticketId, CancellationToken cancellationToken = default);
}
