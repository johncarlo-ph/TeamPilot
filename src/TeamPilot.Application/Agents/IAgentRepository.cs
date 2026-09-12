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
}
