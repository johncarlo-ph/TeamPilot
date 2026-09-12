using TeamPilot.Application.Agents.Dtos;
using TeamPilot.Application.Workflow.Dtos;

namespace TeamPilot.Application.Workflow;

/// <summary>
/// Manages a project's admin-configurable agent workflow: the ordered sequence of agents a
/// ticket's pipeline runs through, replacing the previously fixed
/// Research/Design/Coding/Testing order. See <c>TeamPilot.Application.Orchestration.
/// OrchestrationService</c> for how the sequence this service manages is actually executed.
/// </summary>
public interface IWorkflowService
{
    /// <summary>
    /// Ensures the project has a workflow, creating the default Research → Design → Coding →
    /// Testing sequence (with Testing looping back to Coding, bounded at 3 attempts - the same
    /// behavior the pipeline used to hardcode) if it has none yet. Called once on project
    /// creation, and again defensively before running a ticket's pipeline so projects created
    /// before this existed self-heal.
    /// </summary>
    Task EnsureDefaultWorkflowAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowStageDto>> ListAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Agents that exist in the project but aren't currently part of its workflow - a
    /// freshly created custom agent, or a previously removed default agent.</summary>
    Task<IReadOnlyList<AgentDto>> ListUnscheduledAgentsAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new agent with no seeded instructions. It is not placed into the workflow -
    /// use <see cref="AddExistingAgentAsync"/> once its instructions are complete. Always allowed,
    /// even while the project has an <c>InProgress</c> ticket, since it can't affect any running
    /// pipeline until it's explicitly scheduled.
    /// </summary>
    Task<AgentDto> CreateCustomAgentAsync(Guid projectId, CreateCustomAgentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes a custom agent that isn't currently scheduled into the workflow and
    /// has never been assigned to a ticket - the one case where a real delete (not the usual
    /// "deactivate, never delete" rule) is safe, since there's no history to lose. Rejects a
    /// non-`Custom` agent, one that's still scheduled, or one with any assignment history
    /// (<see cref="Agents.IAgentRepository.HasAssignmentHistoryAsync"/>).
    /// </summary>
    Task DeleteCustomAgentAsync(Guid projectId, Guid agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends an unscheduled agent to the end of the project's workflow. Rejects an agent with
    /// incomplete instructions (<see cref="Domain.Entities.Agent.HasCompleteInstructions"/>) and,
    /// like every other structural change below, is blocked while the project has an
    /// <c>InProgress</c> ticket.
    /// </summary>
    Task<WorkflowStageDto> AddExistingAgentAsync(Guid projectId, AddWorkflowStageRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a stage (soft-remove: only its place in the sequence - the agent and its
    /// instruction history are untouched). Rejects removing a stage another stage's loop-back
    /// still targets.
    /// </summary>
    Task RemoveStageAsync(Guid projectId, Guid stageId, CancellationToken cancellationToken = default);

    /// <summary>Reorders the project's stages to match <paramref name="request"/>'s id list
    /// exactly, renumbering every stage. Rejects a reorder that would leave any loop-back
    /// targeting a stage that's no longer earlier in the sequence.</summary>
    Task<IReadOnlyList<WorkflowStageDto>> ReorderAsync(Guid projectId, ReorderWorkflowRequest request, CancellationToken cancellationToken = default);

    Task<WorkflowStageDto> SetLoopBackAsync(Guid projectId, Guid stageId, SetWorkflowLoopBackRequest request, CancellationToken cancellationToken = default);

    Task<WorkflowStageDto> ClearLoopBackAsync(Guid projectId, Guid stageId, CancellationToken cancellationToken = default);
}
