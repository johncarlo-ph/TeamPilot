using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.InstructionTemplates.Dtos;

public sealed record CreateInstructionTemplateRequest(string Name, AgentRole Role, InstructionType Type, string Content);
