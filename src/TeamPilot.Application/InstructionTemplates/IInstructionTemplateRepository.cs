using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.InstructionTemplates;

public interface IInstructionTemplateRepository
{
    Task<InstructionTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InstructionTemplate>> ListAsync(AgentRole? role, InstructionType? type, CancellationToken cancellationToken = default);

    Task AddAsync(InstructionTemplate template, CancellationToken cancellationToken = default);

    Task DeleteAsync(InstructionTemplate template, CancellationToken cancellationToken = default);
}
