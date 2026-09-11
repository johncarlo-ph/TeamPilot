namespace TeamPilot.Application.Projects.Dtos;

public sealed record CreateProjectRequest(string Name, string? Description, string RepositoryPath);
