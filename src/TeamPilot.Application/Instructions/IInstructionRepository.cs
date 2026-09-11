using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Instructions;

/// <summary>
/// Read-only, lean queries over an agent's instruction history, so callers don't need to
/// load the full <see cref="Agent"/> aggregate just to list or look up versions.
/// </summary>
public interface IInstructionRepository
{
    Task<IReadOnlyList<Instruction>> ListByAgentAsync(Guid agentId, CancellationToken cancellationToken = default);

    Task<Instruction?> GetCurrentAsync(Guid agentId, InstructionType type, CancellationToken cancellationToken = default);
}
