namespace TeamPilot.Application.LiveAgentChat.Dtos;

public sealed record ConversationDto(
    Guid Id,
    Guid ProjectId,
    string Title,
    Guid? CreatedByUserId,
    string? CreatedByName,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);
