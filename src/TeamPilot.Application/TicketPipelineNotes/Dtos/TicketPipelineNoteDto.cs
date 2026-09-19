using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.TicketPipelineNotes.Dtos;

public sealed record TicketPipelineNoteDto(
    Guid Id,
    Guid? AgentId,
    AgentRole? Role,
    string Text,
    DateTime CreatedAtUtc);
