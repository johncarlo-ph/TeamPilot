namespace TeamPilot.Application.Common.Exceptions;

/// <summary>
/// Thrown when an admin tries to change a project's workflow structure (add/remove/reorder a
/// stage, or set/clear a loop-back) while a ticket in that project is <c>InProgress</c> or
/// <c>Blocked</c> - a use-case constraint (it depends on sibling tickets' state), not something
/// <see cref="Domain.Entities.WorkflowStage"/> could enforce on its own. A <c>Blocked</c> ticket
/// still counts because its pipeline is only paused, not finished.
/// </summary>
public sealed class WorkflowLockedException(int lockingTicketCount)
    : Exception($"Cannot modify the pipeline while {lockingTicketCount} ticket(s) are In Progress or Blocked.")
{
}
