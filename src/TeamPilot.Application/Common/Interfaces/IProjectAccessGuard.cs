namespace TeamPilot.Application.Common.Interfaces;

/// <summary>
/// Enforces per-user project scoping on ticket-board operations. Admins bypass this
/// entirely; other roles are only allowed into projects an Admin has assigned them to.
/// </summary>
public interface IProjectAccessGuard
{
    /// <summary>
    /// Throws <see cref="Exceptions.ForbiddenException"/> if the current user cannot access
    /// the given project.
    /// </summary>
    Task EnsureAccessAsync(Guid projectId, CancellationToken cancellationToken = default);
}
