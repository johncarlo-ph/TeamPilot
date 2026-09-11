using TeamPilot.Application.Instructions.Dtos;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Instructions;

public interface IInstructionService
{
    Task<IReadOnlyList<InstructionDto>> ListByAgentAsync(Guid agentId, CancellationToken cancellationToken = default);

    Task<InstructionDto?> GetCurrentAsync(Guid agentId, InstructionType type, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a new instruction version for the agent, superseding the previous current
    /// version of the same type (history is retained, never overwritten).
    /// </summary>
    Task<InstructionDto> AddVersionAsync(Guid agentId, AddInstructionVersionRequest request, CancellationToken cancellationToken = default);
}
