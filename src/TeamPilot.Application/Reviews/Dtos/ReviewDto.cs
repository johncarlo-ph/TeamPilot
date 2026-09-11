using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Reviews.Dtos;

public sealed record ReviewDto(
    Guid Id,
    Guid TicketId,
    string ReviewerName,
    ReviewDecision Decision,
    string? Comments,
    DateTime CreatedAtUtc);
