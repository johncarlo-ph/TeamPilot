using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Agents;

public interface IAgentRepository
{
    /// <summary>
    /// Loads an agent tracked for updates, with its instruction history included so
    /// <see cref="Agent.AddInstructionVersion"/> can compute superseding/versioning correctly.
    /// </summary>
    Task<Agent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Agent>> ListAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the project's single active agent for a pipeline role
    /// (Research/Design/Coding/Testing), or <see langword="null"/> if none is active.
    /// </summary>
    Task<Agent?> GetByProjectAndRoleAsync(Guid projectId, AgentRole role, CancellationToken cancellationToken = default);

    Task AddAsync(Agent agent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes an agent and its instruction history. Only ever called for a
    /// <see cref="AgentRole.Custom"/> agent that has never been scheduled into a project's
    /// workflow and never assigned to a ticket - see
    /// <see cref="HasAssignmentHistoryAsync"/> and <c>WorkflowService.DeleteCustomAgentAsync</c>.
    /// Every other agent is only ever deactivated, never deleted.
    /// </summary>
    void Remove(Agent agent);

    /// <summary>
    /// Whether this agent has ever been assigned to any ticket (a real, permanent
    /// <c>TicketAgentAssignments</c> row referencing it exists) - the signal that it has actual
    /// history worth preserving, regardless of whether it's currently scheduled into a workflow.
    /// </summary>
    Task<bool> HasAssignmentHistoryAsync(Guid agentId, CancellationToken cancellationToken = default);
}
