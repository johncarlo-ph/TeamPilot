using TeamPilot.Domain.Enums;

namespace TeamPilot.Domain.Exceptions;

public sealed class InvalidPipelineRunStateTransitionException : DomainException
{
    public PipelineRunStatus CurrentStatus { get; }

    public string AttemptedAction { get; }

    public InvalidPipelineRunStateTransitionException(PipelineRunStatus currentStatus, string attemptedAction)
        : base($"Cannot {attemptedAction} while the pipeline run status is '{currentStatus}'.")
    {
        CurrentStatus = currentStatus;
        AttemptedAction = attemptedAction;
    }
}
