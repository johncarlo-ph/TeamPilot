namespace TeamPilot.Application.Users.Dtos;

public sealed record SetUserProjectsRequest(IReadOnlyCollection<Guid> ProjectIds);
