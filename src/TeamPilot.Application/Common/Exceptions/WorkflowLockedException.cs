namespace TeamPilot.Application.Common.Exceptions;

/// <summary>
/// Thrown when an admin tries to change a project's workflow structure (add/remove/reorder a
/// stage, or set/clear a loop-back) while a ticket in that project is <c>InProgress</c> - a
/// use-case constraint (it depends on sibling tickets' state), not something
/// <see cref="Domain.Entities.WorkflowStage"/> could enforce on its own.
/// </summary>
public sealed class WorkflowLockedException(int inProgressTicketCount)
    : Exception($"Cannot modify the pipeline while {inProgressTicketCount} ticket(s) are In Progress.")
{
}
