using TeamPilot.Domain.Enums;

namespace TeamPilot.Domain.Exceptions;

public sealed class InvalidConflictStateTransitionException : DomainException
{
    public ConflictStatus CurrentStatus { get; }

    public string AttemptedAction { get; }

    public InvalidConflictStateTransitionException(ConflictStatus currentStatus, string attemptedAction)
        : base($"Cannot {attemptedAction} while the conflict status is '{currentStatus}'.")
    {
        CurrentStatus = currentStatus;
        AttemptedAction = attemptedAction;
    }
}
