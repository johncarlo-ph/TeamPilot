namespace TeamPilot.Application.Sprints.Dtos;

public sealed record SprintDto(
    Guid Id,
    Guid ProjectId,
    string Name,
    string BaseBranch,
    DateTime? SprintStartDate,
    DateTime? SprintEndDate,
    string? SprintGoal,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    TicketStatusCountsDto TicketStatusCounts);
