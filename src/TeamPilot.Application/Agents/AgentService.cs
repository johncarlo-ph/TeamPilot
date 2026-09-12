using FluentValidation;
using TeamPilot.Application.Agents.Dtos;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Extensions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Agents;

public sealed class AgentService(
    IAgentRepository agentRepository,
    IProjectAccessGuard projectAccessGuard,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork,
    IValidator<UpdateAgentConfigurationRequest> updateConfigurationValidator) : IAgentService
{
    private static readonly AgentRole[] PipelineRoles =
        [AgentRole.Research, AgentRole.Design, AgentRole.Coding, AgentRole.Testing];

    public async Task EnsureDefaultAgentsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var created = false;

        foreach (var role in PipelineRoles)
        {
            var existing = await agentRepository.GetByProjectAndRoleAsync(projectId, role, cancellationToken);
            if (existing is not null)
            {
                continue;
            }

            var agent = Agent.Create(projectId, $"{role} Agent", role);
            await agentRepository.AddAsync(agent, cancellationToken);
            created = true;
        }

        if (created)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
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

        await auditLogger.LogActionAsync(AuditEventType.AgentConfigurationUpdated, $"Configuration updated for agent '{agent.Name}'.", cancellationToken);
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

        await auditLogger.LogActionAsync(AuditEventType.AgentStatusUpdated, $"Agent '{agent.Name}' set to {request.Status}.", cancellationToken);
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
