using TeamPilot.Domain.Entities;

namespace TeamPilot.Application.Commits;

/// <summary>
/// Read-only access to commits. New commits are created through the <see cref="Ticket"/>
/// aggregate (<see cref="Ticket.AddCommit"/>), not through this repository.
/// </summary>
public interface ICommitRepository
{
    Task<IReadOnlyList<Commit>> ListByTicketAsync(Guid ticketId, CancellationToken cancellationToken = default);
}
