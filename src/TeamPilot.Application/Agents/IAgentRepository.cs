using TeamPilot.Domain.Entities;

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
    /// Whether the project already has an Orchestrator agent - each project may have only one.
    /// </summary>
    Task<bool> HasOrchestratorAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task AddAsync(Agent agent, CancellationToken cancellationToken = default);
}
