using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Projects.Dtos;

public sealed record ProjectDto(
    Guid Id,
    string Name,
    string Description,
    string RemoteUrl,
    string BaseBranch,
    ProjectStatus Status,
    string? CloneFailureReason,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    TicketStatusCountsDto TicketStatusCounts);
