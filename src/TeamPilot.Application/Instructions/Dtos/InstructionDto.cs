using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Instructions.Dtos;

public sealed record InstructionDto(
    Guid Id,
    Guid AgentId,
    InstructionType Type,
    string Content,
    int Version,
    bool IsCurrent,
    string? CreatedBy,
    DateTime CreatedAtUtc);
