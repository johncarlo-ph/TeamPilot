namespace TeamPilot.Application.Common.Exceptions;

/// <summary>
/// Thrown when an admin tries to remove a project while it still has a ticket <c>InProgress</c>
/// or <c>ForReview</c> - a use-case constraint (it depends on sibling <c>Ticket</c> rows), not
/// something <see cref="Domain.Entities.Project"/> could enforce on its own. Unlike
/// <see cref="WorkflowLockedException"/>, a <c>Blocked</c> ticket does not count here: removal
/// only needs to protect work that's actively running or awaiting approval.
/// </summary>
public sealed class ProjectHasActiveTicketsException(int activeTicketCount)
    : Exception($"Cannot remove the project while {activeTicketCount} ticket(s) are In Progress or For Review.")
{
}
