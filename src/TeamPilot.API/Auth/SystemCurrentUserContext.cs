using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Domain.Enums;

namespace TeamPilot.API.Auth;

/// <summary>
/// Stands in for <see cref="ICurrentUserContext"/> outside of a live HTTP request - currently
/// only <c>IBackgroundTaskRunner</c>'s detached scope (e.g. <c>ApprovalGateService</c>'s
/// RequestChanges pipeline re-run). There's no end user to attribute the work to there, but its
/// authorization was already checked once against the real caller when the original request was
/// handled (before the background work was kicked off), so re-running that same per-user check
/// inside the background task itself would be both meaningless (no user to check) and wrong (it
/// would reject work that's already been authorized). Reports Admin-equivalent access so
/// <c>IProjectAccessGuard</c> short-circuits instead of throwing, while <c>IsAuthenticated</c>
/// stays false so <c>IAuditLogger</c> records these actions with no attributed user rather than
/// a fabricated one.
/// </summary>
public sealed class SystemCurrentUserContext : ICurrentUserContext
{
    public bool IsAuthenticated => false;

    public Guid UserId => Guid.Empty;

    public string? Name => "System";

    public string? Email => null;

    public string? IpAddress => null;

    public IReadOnlyCollection<UserRole> Roles => [UserRole.Admin];

    public bool IsInRole(UserRole role) => role == UserRole.Admin;
}
