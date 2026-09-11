using TeamPilot.Application.AuditLog;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Auth;

/// <summary>
/// Queues an audit entry on the current unit of work - it does not call SaveChanges itself,
/// so it's persisted atomically with whatever operation it's logging.
/// </summary>
public sealed class AuditLogger(IAuditLogRepository auditLogRepository) : IAuditLogger
{
    public async Task LogAsync(AuditEventType eventType, Guid? userId, string? detail, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var entry = AuditLogEntry.Create(eventType, userId, detail, ipAddress);
        await auditLogRepository.AddAsync(entry, cancellationToken);
    }
}
