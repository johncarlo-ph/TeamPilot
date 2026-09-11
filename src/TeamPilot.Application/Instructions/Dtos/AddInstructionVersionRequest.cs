using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Instructions.Dtos;

public sealed record AddInstructionVersionRequest(InstructionType Type, string Content, string? UpdatedBy);
