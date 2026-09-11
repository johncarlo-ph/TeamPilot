namespace TeamPilot.Application.Projects.Dtos;

/// <summary>AccessToken null/blank means "keep the currently stored token" - RemoteUrl is not
/// editable here since it's immutable after creation (see Project.RemoteUrl).</summary>
public sealed record UpdateProjectRequest(string Name, string? Description, string? AccessToken, string BaseBranch);
