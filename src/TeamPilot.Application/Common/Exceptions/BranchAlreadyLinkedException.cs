namespace TeamPilot.Application.Common.Exceptions;

/// <summary>
/// Thrown when linking a branch to a ticket would give two tickets the same branch - a
/// use-case constraint (which existing ticket already claims it), not a rule the <c>Ticket</c>
/// entity could enforce on its own, since it has no visibility into sibling tickets.
/// </summary>
public sealed class BranchAlreadyLinkedException(string branchName, string otherTicketTitle)
    : Exception($"Branch '{branchName}' is already linked to ticket '{otherTicketTitle}' and cannot be linked to another ticket.")
{
}
