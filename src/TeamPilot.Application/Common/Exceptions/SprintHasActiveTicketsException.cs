namespace TeamPilot.Application.Common.Exceptions;

/// <summary>
/// Thrown when an admin tries to remove a sprint while it still has a ticket <c>InProgress</c>
/// or <c>ForReview</c> - mirrors <see cref="ProjectHasActiveTicketsException"/>, scoped to a
/// single sprint's board instead of the whole project.
/// </summary>
public sealed class SprintHasActiveTicketsException(int activeTicketCount)
    : Exception($"Cannot remove the sprint while {activeTicketCount} ticket(s) are In Progress or For Review.")
{
}
