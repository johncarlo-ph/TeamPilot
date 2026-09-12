using FluentValidation;
using TeamPilot.Application.Agents;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Extensions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Instructions.Dtos;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Instructions;

public sealed class InstructionService(
    IAgentRepository agentRepository,
    IInstructionRepository instructionRepository,
    IProjectAccessGuard projectAccessGuard,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork,
    IValidator<AddInstructionVersionRequest> addVersionValidator) : IInstructionService
{
    public async Task<IReadOnlyList<InstructionDto>> ListByAgentAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        await EnsureAgentAccessAsync(agentId, cancellationToken);

        var instructions = await instructionRepository.ListByAgentAsync(agentId, cancellationToken);
        return instructions.Select(ToDto).ToList();
    }

    public async Task<InstructionDto?> GetCurrentAsync(Guid agentId, InstructionType type, CancellationToken cancellationToken = default)
    {
        await EnsureAgentAccessAsync(agentId, cancellationToken);

        var instruction = await instructionRepository.GetCurrentAsync(agentId, type, cancellationToken);
        return instruction is null ? null : ToDto(instruction);
    }

    public async Task<InstructionDto> AddVersionAsync(Guid agentId, AddInstructionVersionRequest request, CancellationToken cancellationToken = default)
    {
        await addVersionValidator.EnsureValidAsync(request, cancellationToken);

        var agent = await agentRepository.GetByIdAsync(agentId, cancellationToken)
            ?? throw new NotFoundException(nameof(Agent), agentId);

        await projectAccessGuard.EnsureAccessAsync(agent.ProjectId, cancellationToken);

        var instruction = agent.AddInstructionVersion(request.Type, request.Content, request.UpdatedBy);

        await auditLogger.LogActionAsync(AuditEventType.AgentInstructionUpdated, $"{request.Type} instructions updated for agent '{agent.Name}' (v{instruction.Version}).", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(instruction);
    }

    private async Task EnsureAgentAccessAsync(Guid agentId, CancellationToken cancellationToken)
    {
        var agent = await agentRepository.GetByIdAsync(agentId, cancellationToken)
            ?? throw new NotFoundException(nameof(Agent), agentId);

        await projectAccessGuard.EnsureAccessAsync(agent.ProjectId, cancellationToken);
    }

    private static InstructionDto ToDto(Instruction instruction) => new(
        instruction.Id,
        instruction.AgentId,
        instruction.Type,
        instruction.Content,
        instruction.Version,
        instruction.IsCurrent,
        instruction.CreatedBy,
        instruction.CreatedAtUtc);
}
