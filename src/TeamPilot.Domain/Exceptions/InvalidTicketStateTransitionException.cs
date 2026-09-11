using TeamPilot.Domain.Enums;

namespace TeamPilot.Domain.Exceptions;

public sealed class InvalidTicketStateTransitionException : DomainException
{
    public TicketStatus CurrentStatus { get; }

    public string AttemptedAction { get; }

    public InvalidTicketStateTransitionException(TicketStatus currentStatus, string attemptedAction)
        : base($"Cannot {attemptedAction} while the ticket status is '{currentStatus}'.")
    {
        CurrentStatus = currentStatus;
        AttemptedAction = attemptedAction;
    }
}
