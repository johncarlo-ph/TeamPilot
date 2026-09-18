namespace TeamPilot.Application.Dashboard.Rows;

/// <summary>
/// Raw projection of a sprint whose end date has passed or is within the at-risk window while it
/// still has unfinished tickets - returned by <see cref="IDashboardRepository.GetAtRiskSprintsAsync"/>
/// and mapped into <see cref="Dtos.AtRiskSprintDto"/> by <see cref="DashboardService"/>.
/// </summary>
public sealed record AtRiskSprintRow(
    Guid Id,
    string Name,
    Guid ProjectId,
    string ProjectName,
    DateTime SprintEndDate,
    int UnfinishedTicketCount);
