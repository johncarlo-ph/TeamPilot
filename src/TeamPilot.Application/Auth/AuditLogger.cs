using TeamPilot.Application.AuditLog;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Auth;

/// <summary>
/// Queues an audit entry on the current unit of work - it does not call SaveChanges itself,
/// so it's persisted atomically with whatever operation it's logging.
/// </summary>
public sealed class AuditLogger(IAuditLogRepository auditLogRepository, ICurrentUserContext currentUser) : IAuditLogger
{
    public async Task LogAsync(AuditEventType eventType, Guid? userId, string? detail, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var entry = AuditLogEntry.Create(eventType, userId, detail, ipAddress);
        await auditLogRepository.AddAsync(entry, cancellationToken);
    }

    public Task LogActionAsync(AuditEventType eventType, string? detail = null, CancellationToken cancellationToken = default) =>
        // UserId is a real (if all-zero) Guid even when unauthenticated (see
        // ICurrentUserContext.UserId), not null - passing it unconditionally would try to FK an
        // audit row to a user that doesn't exist for any caller IsAuthenticated is false for
        // (e.g. SystemCurrentUserContext, used outside a live HTTP request). AuditLogEntry.UserId
        // is nullable specifically for this "no attributable user" case.
        LogAsync(eventType, currentUser.IsAuthenticated ? currentUser.UserId : null, detail, currentUser.IpAddress, cancellationToken);
}
