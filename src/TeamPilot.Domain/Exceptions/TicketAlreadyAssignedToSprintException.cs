namespace TeamPilot.Domain.Exceptions;

public sealed class TicketAlreadyAssignedToSprintException : DomainException
{
    public TicketAlreadyAssignedToSprintException(string ticketTitle)
        : base($"Ticket '{ticketTitle}' is already assigned to a sprint.")
    {
    }
}
