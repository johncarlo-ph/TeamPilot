namespace TeamPilot.Application.Sprints.Dtos;

public sealed record UpdateSprintRequest(
    string Name,
    string BaseBranch,
    DateTime? SprintStartDate = null,
    DateTime? SprintEndDate = null,
    string? SprintGoal = null);
