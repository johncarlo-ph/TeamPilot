namespace TeamPilot.Application.Projects.Dtos;

public sealed record ProjectDto(
    Guid Id,
    string Name,
    string Description,
    string RepositoryPath,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);
