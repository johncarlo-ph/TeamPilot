namespace TeamPilot.Application.Common.Exceptions;

/// <summary>
/// Thrown when an operation that needs a project's sandbox clone (e.g. creating a ticket) is
/// attempted while the project's remote repository is still cloning, or failed to clone - a
/// use-case constraint checked ahead of time so the caller gets a clear error instead of a
/// confusing Git failure against an empty <c>Project.RepositoryPath</c>.
/// </summary>
public sealed class ProjectNotReadyException(string projectName)
    : Exception($"Project '{projectName}' is not ready yet - its repository is still being cloned, or the clone failed.")
{
}
