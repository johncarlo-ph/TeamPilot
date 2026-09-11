using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.AuditLog.Dtos;

public sealed record AuditLogEntryDto(
    Guid Id,
    Guid? UserId,
    AuditEventType EventType,
    string? Detail,
    string? IpAddress,
    DateTime CreatedAtUtc);
