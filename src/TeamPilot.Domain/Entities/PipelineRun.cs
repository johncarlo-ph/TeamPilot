using TeamPilot.Domain.Common;
using TeamPilot.Domain.Enums;
using TeamPilot.Domain.Exceptions;

namespace TeamPilot.Domain.Entities;

/// <summary>
/// A CI/CD pipeline run for a project, tracking status only (Queued -&gt; Running -&gt;
/// Succeeded/Failed). No real build/test/deploy execution happens here - this is the record
/// a future real CI runner would call back into via <see cref="Start"/>/<see cref="Complete"/>.
/// </summary>
public class PipelineRun : Entity
{
    public Guid ProjectId { get; private set; }

    /// <summary>
    /// The ticket whose merge triggered this run, or <c>null</c> for a manually triggered run.
    /// </summary>
    public Guid? TicketId { get; private set; }

    public PipelineRunStatus Status { get; private set; }

    public string TriggerReason { get; private set; } = string.Empty;

    public string? LogOutput { get; private set; }

    public DateTime? StartedAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    private PipelineRun()
    {
    }

    public static PipelineRun Create(Guid projectId, Guid? ticketId, string triggerReason)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project id is required.", nameof(projectId));
        }

        if (string.IsNullOrWhiteSpace(triggerReason))
        {
            throw new ArgumentException("Trigger reason is required.", nameof(triggerReason));
        }

        return new PipelineRun
        {
            ProjectId = projectId,
            TicketId = ticketId,
            TriggerReason = triggerReason,
            Status = PipelineRunStatus.Queued,
        };
    }

    public void Start()
    {
        if (Status != PipelineRunStatus.Queued)
        {
            throw new InvalidPipelineRunStateTransitionException(Status, "start");
        }

        Status = PipelineRunStatus.Running;
        StartedAtUtc = DateTime.UtcNow;
        MarkUpdated();
    }

    public void Complete(bool succeeded, string? logOutput)
    {
        if (Status != PipelineRunStatus.Running)
        {
            throw new InvalidPipelineRunStateTransitionException(Status, "complete");
        }

        Status = succeeded ? PipelineRunStatus.Succeeded : PipelineRunStatus.Failed;
        LogOutput = logOutput;
        CompletedAtUtc = DateTime.UtcNow;
        MarkUpdated();
    }
}
