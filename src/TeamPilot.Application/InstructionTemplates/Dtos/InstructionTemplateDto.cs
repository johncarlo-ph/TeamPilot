using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.InstructionTemplates.Dtos;

public sealed record InstructionTemplateDto(
    Guid Id,
    string Name,
    AgentRole Role,
    InstructionType Type,
    string Content,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);
