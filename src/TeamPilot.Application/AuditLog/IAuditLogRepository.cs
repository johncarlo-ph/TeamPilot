using TeamPilot.Domain.Entities;

namespace TeamPilot.Application.AuditLog;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditLogEntry>> ListAsync(int skip, int take, CancellationToken cancellationToken = default);
}
