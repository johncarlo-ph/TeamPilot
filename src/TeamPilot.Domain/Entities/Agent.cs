using TeamPilot.Domain.Common;
using TeamPilot.Domain.Enums;
using TeamPilot.Domain.Exceptions;

namespace TeamPilot.Domain.Entities;

/// <summary>
/// An AI agent (orchestrator, research, design, or coding) that can be assigned to tickets.
/// Owns the versioned instruction history that governs its behavior.
/// </summary>
public class Agent : Entity
{
    private readonly List<Instruction> _instructions = [];

    public string Name { get; private set; } = string.Empty;

    public AgentRole Role { get; private set; }

    public AgentStatus Status { get; private set; }

    public string ConfigurationJson { get; private set; } = "{}";

    public Guid ProjectId { get; private set; }

    public IReadOnlyCollection<Instruction> Instructions => _instructions.AsReadOnly();

    /// <summary>
    /// Whether this agent has a current version of every <see cref="InstructionType"/>
    /// (Constitution, Guideline, and Requirement). The 4 default pipeline roles and
    /// <see cref="AgentRole.LiveAgent"/> are always complete from the moment they're created
    /// (seeded by <see cref="AgentDefaultInstructions"/>); only an <see cref="AgentRole.Custom"/>
    /// agent can start incomplete. Because <see cref="AddInstructionVersion"/> only ever appends
    /// or supersedes a type - it never removes one - an agent that becomes complete can never
    /// become incomplete again, which is what lets callers gate on this once, when the agent is
    /// added to a project's workflow, rather than re-checking it on every use.
    /// </summary>
    public bool HasCompleteInstructions =>
        _instructions.Where(i => i.IsCurrent).Select(i => i.Type).Distinct().Count() == Enum.GetValues<InstructionType>().Length;

    private Agent()
    {
    }

    public static Agent Create(Guid projectId, string name, AgentRole role, string configurationJson = "{}")
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project id is required.", nameof(projectId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Agent name is required.", nameof(name));
        }

        var agent = new Agent
        {
            ProjectId = projectId,
            Name = name.Trim(),
            Role = role,
            Status = AgentStatus.Active,
            ConfigurationJson = string.IsNullOrWhiteSpace(configurationJson) ? "{}" : configurationJson,
        };

        foreach (var (type, content) in AgentDefaultInstructions.For(role))
        {
            agent.AddInstructionVersion(type, content, AgentDefaultInstructions.Author);
        }

        return agent;
    }

    public void Activate()
    {
        Status = AgentStatus.Active;
        MarkUpdated();
    }

    public void Deactivate()
    {
        Status = AgentStatus.Inactive;
        MarkUpdated();
    }

    public void UpdateConfiguration(string configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            throw new ArgumentException("Configuration JSON is required.", nameof(configurationJson));
        }

        ConfigurationJson = configurationJson;
        MarkUpdated();
    }

    /// <summary>
    /// Appends a new version of an instruction, superseding the previous current version of the same type.
    /// Prior versions are retained for history rather than overwritten. Once a default/standing role
    /// (anything but <see cref="AgentRole.Custom"/>) has its initial Constitution seeded by
    /// <see cref="Create"/>, that Constitution can never be re-versioned - it is fixed so the orchestration
    /// pipeline can rely on what each stage fundamentally is. Use this agent's Guideline or Requirement
    /// instructions to tune its behavior instead. A Custom agent's Constitution has no such restriction.
    /// </summary>
    public Instruction AddInstructionVersion(InstructionType type, string content, string? updatedBy)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Instruction content is required.", nameof(content));
        }

        var versionsOfType = _instructions.Where(i => i.Type == type).ToList();

        if (type == InstructionType.Constitution && Role != AgentRole.Custom && versionsOfType.Count > 0)
        {
            throw new ConstitutionEditNotAllowedException(Role);
        }

        foreach (var current in versionsOfType.Where(i => i.IsCurrent))
        {
            current.MarkSuperseded();
        }

        var nextVersion = versionsOfType.Count == 0 ? 1 : versionsOfType.Max(i => i.Version) + 1;
        var instruction = Instruction.Create(Id, type, content, nextVersion, updatedBy);
        _instructions.Add(instruction);
        MarkUpdated();

        return instruction;
    }
}
