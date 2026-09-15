using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.TicketAgentEvents.Dtos;

public sealed record TicketAgentEventDto(
    Guid Id,
    Guid TicketId,
    Guid? AgentId,
    AgentRole? Role,
    TicketAgentEventKind Kind,
    string? Result,
    DateTime CreatedAtUtc);
