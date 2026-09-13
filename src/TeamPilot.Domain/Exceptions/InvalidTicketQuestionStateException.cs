using TeamPilot.Domain.Enums;

namespace TeamPilot.Domain.Exceptions;

public sealed class InvalidTicketQuestionStateException : DomainException
{
    public TicketQuestionStatus CurrentStatus { get; }

    public string AttemptedAction { get; }

    public InvalidTicketQuestionStateException(TicketQuestionStatus currentStatus, string attemptedAction)
        : base($"Cannot {attemptedAction} while the ticket question status is '{currentStatus}'.")
    {
        CurrentStatus = currentStatus;
        AttemptedAction = attemptedAction;
    }
}
