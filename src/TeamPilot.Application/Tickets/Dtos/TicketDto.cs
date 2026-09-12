using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Tickets.Dtos;

public sealed record TicketDto(
    Guid Id,
    Guid ProjectId,
    string Title,
    string Description,
    TicketStatus Status,
    string? BranchName,
    string? CancellationReason,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);
