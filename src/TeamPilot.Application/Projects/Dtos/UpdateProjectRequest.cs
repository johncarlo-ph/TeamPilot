namespace TeamPilot.Application.Projects.Dtos;

public sealed record UpdateProjectRequest(string Name, string? Description, string RepositoryPath);
