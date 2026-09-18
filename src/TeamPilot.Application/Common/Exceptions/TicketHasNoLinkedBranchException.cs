namespace TeamPilot.Application.Common.Exceptions;

/// <summary>
/// Thrown when approving a ticket that has no linked branch to merge - approval fundamentally
/// requires something to merge, so this is a use-case precondition rather than an entity
/// invariant (<see cref="Domain.Entities.Ticket"/> allows reaching <c>ForReview</c> without a
/// branch, e.g. a ticket rejected and reopened before one was ever linked).
/// </summary>
public sealed class TicketHasNoLinkedBranchException(string ticketTitle)
    : Exception($"Ticket '{ticketTitle}' has no linked branch to merge.")
{
}
