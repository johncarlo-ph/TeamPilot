using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.LiveAgentChat.Dtos;

public sealed record ChatMessageDto(
    Guid Id,
    ChatMessageRole Role,
    string Content,
    string? ProposedTicketTitle,
    string? ProposedTicketDescription,
    DateTime CreatedAtUtc);
