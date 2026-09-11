using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Agents.Dtos;

public sealed record AgentDto(
    Guid Id,
    Guid ProjectId,
    string Name,
    AgentRole Role,
    AgentStatus Status,
    string ConfigurationJson,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);
