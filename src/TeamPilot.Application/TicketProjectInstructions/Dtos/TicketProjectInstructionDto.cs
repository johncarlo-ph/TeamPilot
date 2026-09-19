namespace TeamPilot.Application.TicketProjectInstructions.Dtos;

public sealed record TicketProjectInstructionDto(
    Guid Id,
    Guid? AgentId,
    Guid ReferencedProjectId,
    string Text,
    DateTime CreatedAtUtc);
