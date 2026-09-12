using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.AuditLog.Dtos;

public sealed record AuditLogEntryDto(
    Guid Id,
    Guid? UserId,
    string? UserName,
    AuditEventType EventType,
    string? Detail,
    string? IpAddress,
    DateTime CreatedAtUtc);
