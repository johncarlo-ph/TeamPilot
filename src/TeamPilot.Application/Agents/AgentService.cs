using FluentValidation;
using TeamPilot.Application.Agents.Dtos;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Extensions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Projects;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using TeamPilot.Domain.Exceptions;

namespace TeamPilot.Application.Agents;

public sealed class AgentService(
    IAgentRepository agentRepository,
    IProjectRepository projectRepository,
    IProjectAccessGuard projectAccessGuard,
    IUnitOfWork unitOfWork,
    IValidator<CreateAgentRequest> createValidator,
    IValidator<UpdateAgentConfigurationRequest> updateConfigurationValidator) : IAgentService
{
    public async Task<AgentDto> CreateAsync(Guid projectId, CreateAgentRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.EnsureValidAsync(request, cancellationToken);
        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);

        _ = await projectRepository.GetByIdAsync(projectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), projectId);

        if (request.Role == AgentRole.Orchestrator && await agentRepository.HasOrchestratorAsync(projectId, cancellationToken))
        {
            throw new ProjectAlreadyHasOrchestratorException(projectId);
        }

        var agent = Agent.Create(projectId, request.Name, request.Role, request.ConfigurationJson ?? "{}");
        await agentRepository.AddAsync(agent, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(agent);
    }

    public async Task<AgentDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var agent = await agentRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Agent), id);

        await projectAccessGuard.EnsureAccessAsync(agent.ProjectId, cancellationToken);

        return ToDto(agent);
    }

    public async Task<IReadOnlyList<AgentDto>> ListAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);

        var agents = await agentRepository.ListAsync(projectId, cancellationToken);
        return agents.Select(ToDto).ToList();
    }

    public async Task<AgentDto> UpdateConfigurationAsync(Guid id, UpdateAgentConfigurationRequest request, CancellationToken cancellationToken = default)
    {
        await updateConfigurationValidator.EnsureValidAsync(request, cancellationToken);

        var agent = await agentRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Agent), id);

        await projectAccessGuard.EnsureAccessAsync(agent.ProjectId, cancellationToken);

        agent.UpdateConfiguration(request.ConfigurationJson);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(agent);
    }

    public async Task<AgentDto> UpdateStatusAsync(Guid id, UpdateAgentStatusRequest request, CancellationToken cancellationToken = default)
    {
        var agent = await agentRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Agent), id);

        await projectAccessGuard.EnsureAccessAsync(agent.ProjectId, cancellationToken);

        if (request.Status == AgentStatus.Active)
        {
            agent.Activate();
        }
        else
        {
            agent.Deactivate();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(agent);
    }

    private static AgentDto ToDto(Agent agent) => new(
        agent.Id,
        agent.ProjectId,
        agent.Name,
        agent.Role,
        agent.Status,
        agent.ConfigurationJson,
        agent.CreatedAtUtc,
        agent.UpdatedAtUtc);
}
