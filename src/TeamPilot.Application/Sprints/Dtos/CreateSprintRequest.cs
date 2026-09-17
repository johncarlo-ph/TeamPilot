namespace TeamPilot.Application.Sprints.Dtos;

public sealed record CreateSprintRequest(
    string Name,
    string? BaseBranch,
    DateTime? SprintStartDate = null,
    DateTime? SprintEndDate = null,
    string? SprintGoal = null);
