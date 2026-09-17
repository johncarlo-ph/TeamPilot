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
    /// Lean, read-only listing of a sprint's ticket board.
    /// </summary>
    Task<IReadOnlyList<Ticket>> ListAsync(Guid sprintId, TicketStatus? status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lean, read-only listing of every ticket across a project's sprints - used by the Live
    /// Agent chat's read-only ticket tool, which reasons about the whole project rather than one
    /// sprint at a time.
    /// </summary>
    Task<IReadOnlyList<Ticket>> ListByProjectAsync(Guid projectId, TicketStatus? status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lean, read-only listing of a project's backlog - tickets with no <c>SprintId</c> yet.
    /// </summary>
    Task<IReadOnlyList<Ticket>> ListBacklogAsync(Guid projectId, TicketStatus? status, CancellationToken cancellationToken = default);

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
    /// at most one ticket. Project-scoped (not sprint-scoped) because every sprint in a project
    /// shares the same physical Git clone, so branch names must be unique across all of them.
    /// Lean, read-only (no children loaded).
    /// </summary>
    Task<Ticket?> GetByBranchNameAsync(Guid projectId, string branchName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lean existence check for whether the project has any ticket in the given status, across
    /// all its sprints - used by <c>ProjectService.RemoveAsync</c> to block removing a project
    /// with active work.
    /// </summary>
    Task<int> CountByStatusAsync(Guid projectId, TicketStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Same as <see cref="CountByStatusAsync"/> but scoped to a single sprint - used by
    /// <c>SprintService.RemoveAsync</c> to block removing a sprint with active work.
    /// </summary>
    Task<int> CountByStatusBySprintAsync(Guid sprintId, TicketStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ticket counts grouped by status for each of the given sprints, in one query - used by
    /// <c>SprintService</c> to populate the sprint list's summary badges without an N+1 fetch
    /// per sprint. A sprint with no tickets, or no tickets in a given status, simply has no
    /// entry for it - callers default missing statuses to zero.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyDictionary<TicketStatus, int>>> GetStatusCountsBySprintAsync(
        IReadOnlyCollection<Guid> sprintIds, CancellationToken cancellationToken = default);
}
