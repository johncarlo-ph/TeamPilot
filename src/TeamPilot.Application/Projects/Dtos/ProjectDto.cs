namespace TeamPilot.Application.Projects.Dtos;

public sealed record ProjectDto(
    Guid Id,
    string Name,
    string Description,
    string RemoteUrl,
    string BaseBranch,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);
