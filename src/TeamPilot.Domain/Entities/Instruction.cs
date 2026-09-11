using TeamPilot.Domain.Common;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Domain.Entities;

/// <summary>
/// An immutable, versioned instruction (constitution/guideline/requirement) belonging to an <see cref="Agent"/>.
/// New content is appended as a new version rather than overwriting the previous one.
/// </summary>
public class Instruction : Entity
{
    public Guid AgentId { get; private set; }

    public InstructionType Type { get; private set; }

    public string Content { get; private set; } = string.Empty;

    public int Version { get; private set; }

    public bool IsCurrent { get; private set; }

    public string? CreatedBy { get; private set; }

    private Instruction()
    {
    }

    internal static Instruction Create(Guid agentId, InstructionType type, string content, int version, string? createdBy)
    {
        return new Instruction
        {
            AgentId = agentId,
            Type = type,
            Content = content,
            Version = version,
            IsCurrent = true,
            CreatedBy = createdBy,
        };
    }

    internal void MarkSuperseded()
    {
        IsCurrent = false;
        MarkUpdated();
    }
}
