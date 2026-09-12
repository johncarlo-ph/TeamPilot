using TeamPilot.Application.InstructionTemplates.Dtos;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.InstructionTemplates;

public interface IInstructionTemplateService
{
    Task<InstructionTemplateDto> CreateAsync(CreateInstructionTemplateRequest request, CancellationToken cancellationToken = default);

    Task<InstructionTemplateDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InstructionTemplateDto>> ListAsync(AgentRole? role, InstructionType? type, CancellationToken cancellationToken = default);

    Task<InstructionTemplateDto> UpdateAsync(Guid id, UpdateInstructionTemplateRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
