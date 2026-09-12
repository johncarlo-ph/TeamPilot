using TeamPilot.Application.Instructions;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Agents;

/// <summary>
/// Assembles an agent's current Constitution/Guideline/Requirement instructions into the single
/// text block prepended to its LLM prompts. Shared by <see cref="Orchestration.OrchestrationService"/>
/// (pipeline agents) and the Live Agent chat service, so both build "standing instructions" the
/// same way instead of duplicating this logic.
/// </summary>
public static class AgentInstructionsFormatter
{
    private static readonly InstructionType[] TypesInOrder =
        [InstructionType.Constitution, InstructionType.Guideline, InstructionType.Requirement];

    public static async Task<string?> GetInstructionsBlockAsync(
        IInstructionRepository instructionRepository,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        var sections = new List<string>();

        foreach (var type in TypesInOrder)
        {
            var instruction = await instructionRepository.GetCurrentAsync(agentId, type, cancellationToken);
            if (instruction is not null && !string.IsNullOrWhiteSpace(instruction.Content))
            {
                sections.Add($"{type}:\n{instruction.Content}");
            }
        }

        return sections.Count == 0 ? null : string.Join("\n\n", sections);
    }

    public static string FormatInstructions(string? instructions) =>
        string.IsNullOrWhiteSpace(instructions) ? string.Empty : $"Standing instructions for this agent:\n{instructions}\n\n";
}
