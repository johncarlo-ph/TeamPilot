namespace TeamPilot.Application.Dashboard.Dtos;

public sealed record AtRiskSprintDto(
    Guid Id,
    string Name,
    Guid ProjectId,
    string ProjectName,
    DateTime SprintEndDate,
    int UnfinishedTicketCount);
