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

    /// <summary>
    /// Reads the ticket's status directly from the database, bypassing the change tracker -
    /// unlike <see cref="GetByIdAsync"/>, this always reflects another request's concurrent
    /// update instead of returning an already-tracked (and possibly stale) instance from this
    /// same <c>DbContext</c>'s identity map. Used by <c>OrchestrationService</c> to notice a
    /// ticket was cancelled while its pipeline is still running.
    /// </summary>
    Task<TicketStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds whichever ticket in <paramref name="projectId"/> already has
    /// <paramref name="branchName"/> linked, if any - used to enforce that a branch belongs to
    /// at most one ticket. Lean, read-only (no children loaded).
    /// </summary>
    Task<Ticket?> GetByBranchNameAsync(Guid projectId, string branchName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lean existence check for whether the project has any ticket in the given status -
    /// used by <c>WorkflowService</c> to lock pipeline-structure edits while a ticket is
    /// <see cref="TicketStatus.InProgress"/>, without loading full ticket rows.
    /// </summary>
    Task<int> CountByStatusAsync(Guid projectId, TicketStatus status, CancellationToken cancellationToken = default);
}
