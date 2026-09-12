using TeamPilot.Domain.Common;

namespace TeamPilot.Domain.Entities;

/// <summary>
/// One position in a project's admin-configurable agent workflow - the ordered sequence of
/// agents a ticket's pipeline runs through (see
/// <see cref="TeamPilot.Application.Orchestration.IOrchestrationService"/>). References its
/// <see cref="Project"/> and <see cref="Agent"/> by id only, matching how <see cref="Commit"/>/
/// <see cref="Review"/>/<see cref="Conflict"/> relate to <see cref="Ticket"/> - see
/// docs/domain.md.
/// </summary>
public class WorkflowStage : Entity
{
    public Guid ProjectId { get; private set; }

    public Guid AgentId { get; private set; }

    /// <summary>This stage's position in the project's sequence (0-based). What actually decides
    /// execution order for a ticket's pipeline - not merely a UI sort key.</summary>
    public int Order { get; private set; }

    /// <summary>
    /// When set, a FAIL verdict from this stage jumps execution back to the stage with this id
    /// instead of advancing, up to <see cref="MaxLoopIterations"/> times. Whether the target is
    /// actually an earlier stage in the same project can only be checked with the full sequence
    /// in view, so that validation belongs to the caller (see
    /// <c>TeamPilot.Application.Workflow.WorkflowService</c>), not this entity.
    /// </summary>
    public Guid? LoopBackToStageId { get; private set; }

    /// <summary>Only meaningful alongside <see cref="LoopBackToStageId"/>: the total number of
    /// times this stage may run (its first attempt included) before the pipeline gives up on the
    /// loop-back and advances anyway.</summary>
    public int? MaxLoopIterations { get; private set; }

    private WorkflowStage()
    {
    }

    public static WorkflowStage Create(Guid projectId, Guid agentId, int order)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project id is required.", nameof(projectId));
        }

        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("Agent id is required.", nameof(agentId));
        }

        if (order < 0)
        {
            throw new ArgumentException("Order cannot be negative.", nameof(order));
        }

        return new WorkflowStage
        {
            ProjectId = projectId,
            AgentId = agentId,
            Order = order,
        };
    }

    /// <summary>
    /// Repositions this stage within its project's sequence. Renumbering every other stage, and
    /// validating that no stage's loop-back target ends up later than the stage itself, is the
    /// caller's responsibility - a single stage has no visibility into its siblings.
    /// </summary>
    public void MoveTo(int order)
    {
        if (order < 0)
        {
            throw new ArgumentException("Order cannot be negative.", nameof(order));
        }

        Order = order;
        MarkUpdated();
    }

    /// <summary>
    /// Declares that a FAIL verdict from this stage should jump execution back to an earlier
    /// stage, up to <paramref name="maxIterations"/> times, before giving up and advancing anyway.
    /// Whether <paramref name="targetStageId"/> actually is an earlier stage in the same project
    /// is validated by the caller, which has visibility into the full sequence.
    /// </summary>
    public void SetLoopBack(Guid targetStageId, int maxIterations)
    {
        if (targetStageId == Guid.Empty)
        {
            throw new ArgumentException("Target stage id is required.", nameof(targetStageId));
        }

        if (targetStageId == Id)
        {
            throw new ArgumentException("A stage cannot loop back to itself.", nameof(targetStageId));
        }

        if (maxIterations <= 0)
        {
            throw new ArgumentException("Max loop iterations must be positive.", nameof(maxIterations));
        }

        LoopBackToStageId = targetStageId;
        MaxLoopIterations = maxIterations;
        MarkUpdated();
    }

    public void ClearLoopBack()
    {
        LoopBackToStageId = null;
        MaxLoopIterations = null;
        MarkUpdated();
    }
}
