using TeamPilot.Application.Agents.Dtos;

namespace TeamPilot.Application.Agents;

public interface IAgentService
{
    /// <summary>
    /// Creates an agent under a project. Throws <c>ProjectAlreadyHasOrchestratorException</c>
    /// if <see cref="CreateAgentRequest.Role"/> is Orchestrator and the project already has one.
    /// </summary>
    Task<AgentDto> CreateAsync(Guid projectId, CreateAgentRequest request, CancellationToken cancellationToken = default);

    Task<AgentDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentDto>> ListAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<AgentDto> UpdateConfigurationAsync(Guid id, UpdateAgentConfigurationRequest request, CancellationToken cancellationToken = default);

    Task<AgentDto> UpdateStatusAsync(Guid id, UpdateAgentStatusRequest request, CancellationToken cancellationToken = default);
}
