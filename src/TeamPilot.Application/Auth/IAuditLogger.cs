using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Auth;

public interface IAuditLogger
{
    Task LogAsync(AuditEventType eventType, Guid? userId, string? detail, string? ipAddress, CancellationToken cancellationToken = default);
}
