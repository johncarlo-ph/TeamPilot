using Microsoft.EntityFrameworkCore;
using TeamPilot.Application.AuditLog;
using TeamPilot.Domain.Entities;
using TeamPilot.Infrastructure.Persistence;

namespace TeamPilot.Infrastructure.Repositories;

public class AuditLogRepository(TeamPilotDbContext dbContext) : IAuditLogRepository
{
    public async Task AddAsync(AuditLogEntry entry, CancellationToken cancellationToken = default) =>
        await dbContext.AuditLogEntries.AddAsync(entry, cancellationToken);

    public async Task<IReadOnlyList<AuditLogEntry>> ListAsync(int skip, int take, CancellationToken cancellationToken = default) =>
        await dbContext.AuditLogEntries
            .AsNoTracking()
            .OrderByDescending(a => a.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
}
