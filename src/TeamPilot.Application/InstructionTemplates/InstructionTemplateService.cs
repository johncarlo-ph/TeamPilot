using FluentValidation;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Extensions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.InstructionTemplates.Dtos;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.InstructionTemplates;

public sealed class InstructionTemplateService(
    IInstructionTemplateRepository repository,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork,
    IValidator<CreateInstructionTemplateRequest> createValidator,
    IValidator<UpdateInstructionTemplateRequest> updateValidator) : IInstructionTemplateService
{
    public async Task<InstructionTemplateDto> CreateAsync(CreateInstructionTemplateRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.EnsureValidAsync(request, cancellationToken);

        var template = InstructionTemplate.Create(request.Name, request.Role, request.Type, request.Content);
        await repository.AddAsync(template, cancellationToken);

        await auditLogger.LogActionAsync(AuditEventType.InstructionTemplateCreated, $"Instruction template '{template.Name}' created.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(template);
    }

    public async Task<InstructionTemplateDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await repository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(InstructionTemplate), id);

        return ToDto(template);
    }

    public async Task<IReadOnlyList<InstructionTemplateDto>> ListAsync(AgentRole? role, InstructionType? type, CancellationToken cancellationToken = default)
    {
        var templates = await repository.ListAsync(role, type, cancellationToken);
        return templates.Select(ToDto).ToList();
    }

    public async Task<InstructionTemplateDto> UpdateAsync(Guid id, UpdateInstructionTemplateRequest request, CancellationToken cancellationToken = default)
    {
        await updateValidator.EnsureValidAsync(request, cancellationToken);

        var template = await repository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(InstructionTemplate), id);

        template.Update(request.Name, request.Content);

        await auditLogger.LogActionAsync(AuditEventType.InstructionTemplateUpdated, $"Instruction template '{template.Name}' updated.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(template);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await repository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(InstructionTemplate), id);

        await repository.DeleteAsync(template, cancellationToken);

        await auditLogger.LogActionAsync(AuditEventType.InstructionTemplateDeleted, $"Instruction template '{template.Name}' deleted.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static InstructionTemplateDto ToDto(InstructionTemplate template) => new(
        template.Id,
        template.Name,
        template.Role,
        template.Type,
        template.Content,
        template.CreatedAtUtc,
        template.UpdatedAtUtc);
}
