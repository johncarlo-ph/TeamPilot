using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Auth;

public interface IAuditLogger
{
    Task LogAsync(AuditEventType eventType, Guid? userId, string? detail, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs an audit entry for the current authenticated caller - user id and IP address are
    /// read from <see cref="Common.Interfaces.ICurrentUserContext"/> so every UI action can be
    /// audited with a one-line call instead of threading those values through every service.
    /// </summary>
    Task LogActionAsync(AuditEventType eventType, string? detail = null, CancellationToken cancellationToken = default);
}
