using TeamPilot.Application.Agents.Dtos;

namespace TeamPilot.Application.Agents;

public interface IAgentService
{
    /// <summary>
    /// Ensures the project has one active agent for each pipeline role
    /// (Research/Design/Coding/Testing), creating whichever are missing. Called on project
    /// creation, and again defensively before running a ticket's pipeline so projects created
    /// before this existed self-heal.
    /// </summary>
    Task EnsureDefaultAgentsAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<AgentDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentDto>> ListAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<AgentDto> UpdateConfigurationAsync(Guid id, UpdateAgentConfigurationRequest request, CancellationToken cancellationToken = default);

    Task<AgentDto> UpdateStatusAsync(Guid id, UpdateAgentStatusRequest request, CancellationToken cancellationToken = default);
}
