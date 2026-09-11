using TeamPilot.Domain.Common;
using TeamPilot.Domain.Enums;

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
    /// Prior versions are retained for history rather than overwritten.
    /// </summary>
    public Instruction AddInstructionVersion(InstructionType type, string content, string? updatedBy)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Instruction content is required.", nameof(content));
        }

        var versionsOfType = _instructions.Where(i => i.Type == type).ToList();

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
