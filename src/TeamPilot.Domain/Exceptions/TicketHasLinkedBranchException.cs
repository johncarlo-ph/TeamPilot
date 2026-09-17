namespace TeamPilot.Domain.Exceptions;

public sealed class TicketHasLinkedBranchException : DomainException
{
    public TicketHasLinkedBranchException(string ticketTitle, string attemptedAction)
        : base($"Ticket '{ticketTitle}' can't be {attemptedAction} while it has a linked branch.")
    {
    }
}
