using TeamPilot.Domain.Common;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Domain.Entities;

/// <summary>
/// A reusable, admin-managed instruction an admin can pick from when editing a real
/// <see cref="Agent"/>'s instructions, to avoid retyping the same guidance across projects.
/// Unlike <see cref="Instruction"/>, this is a flat, mutable catalog entry - it has no version
/// history of its own; only what gets applied to a real agent is versioned.
/// </summary>
public class InstructionTemplate : Entity
{
    public string Name { get; private set; } = string.Empty;

    public AgentRole Role { get; private set; }

    public InstructionType Type { get; private set; }

    public string Content { get; private set; } = string.Empty;

    private InstructionTemplate()
    {
    }

    public static InstructionTemplate Create(string name, AgentRole role, InstructionType type, string content)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Template name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Template content is required.", nameof(content));
        }

        return new InstructionTemplate
        {
            Name = name.Trim(),
            Role = role,
            Type = type,
            Content = content,
        };
    }

    public void Update(string name, string content)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Template name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Template content is required.", nameof(content));
        }

        Name = name.Trim();
        Content = content;
        MarkUpdated();
    }
}
