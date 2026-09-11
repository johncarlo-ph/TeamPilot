using TeamPilot.Domain.Common;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Domain.Entities;

/// <summary>
/// An immutable, append-only record of a security-relevant event (login, logout, token
/// refresh). <see cref="UserId"/> is nullable since a failed login may never resolve to a
/// known user.
/// </summary>
public class AuditLogEntry : Entity
{
    public Guid? UserId { get; private set; }

    public AuditEventType EventType { get; private set; }

    public string? Detail { get; private set; }

    public string? IpAddress { get; private set; }

    private AuditLogEntry()
    {
    }

    public static AuditLogEntry Create(AuditEventType eventType, Guid? userId, string? detail, string? ipAddress) =>
        new()
        {
            EventType = eventType,
            UserId = userId,
            Detail = detail,
            IpAddress = ipAddress,
        };
}
