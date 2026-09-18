using TeamPilot.Application.Dashboard.Rows;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Dashboard;

/// <summary>
/// Cross-aggregate read queries backing the projects landing page's dashboard summary. Unlike
/// every other repository (each scoped to its own aggregate - <c>Project</c>, <c>Sprint</c>, or
/// <c>Ticket</c> - referencing the others by id only, per their docstrings), this is the one place
/// allowed to join across all three directly, since a dashboard rollup has no aggregate of its
/// own. Every method takes the caller's accessible project ids (already filtered for
/// removed/unassigned projects by <see cref="DashboardService"/>) so results never leak data from
/// a project the current user can't see.
/// </summary>
public interface IDashboardRepository
{
    Task<int> CountActiveSprintsAsync(DateTime asOfUtc, IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default);

    /// <summary>Ticket counts by status across every accessible project. Cancelled is
    /// intentionally excluded, matching <c>TicketStatusCountsDto</c>'s existing convention.</summary>
    Task<IReadOnlyDictionary<TicketStatus, int>> GetGlobalTicketStatusCountsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default);

    /// <summary>Every Blocked or ForReview ticket across accessible projects, newest first.</summary>
    Task<IReadOnlyList<AttentionTicketRow>> GetAttentionTicketsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default);

    /// <summary>Sprints whose end date is on or before <paramref name="thresholdUtc"/> (covers
    /// both "ending soon" and already past-due) that still have at least one unfinished
    /// (InProgress/ForReview/Blocked) ticket, soonest end date first.</summary>
    Task<IReadOnlyList<AtRiskSprintRow>> GetAtRiskSprintsAsync(DateTime thresholdUtc, IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default);
}
