using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamPilot.Application.AuditLog;
using TeamPilot.Application.AuditLog.Dtos;
using TeamPilot.Application.Users;
using TeamPilot.Domain.Entities;

namespace TeamPilot.API.Controllers;

[ApiController]
[Route("api/audit-log")]
[Authorize(Roles = "Admin")]
public class AuditLogController(IAuditLogRepository auditLogRepository, IUserRepository userRepository) : ControllerBase
{
    private const int DefaultPageSize = 50;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AuditLogEntryDto>>> List(
        [FromQuery] int skip,
        [FromQuery] int take,
        CancellationToken cancellationToken)
    {
        var pageSize = take <= 0 ? DefaultPageSize : Math.Min(take, 200);
        var entries = await auditLogRepository.ListAsync(Math.Max(skip, 0), pageSize, cancellationToken);

        // The entry only stores a UserId - names are resolved here so the UI never has to show
        // a raw guid for a human-facing log.
        var users = await userRepository.ListAsync(cancellationToken);
        var namesById = users.ToDictionary(u => u.Id, u => u.Name);

        return Ok(entries.Select(entry => ToDto(entry, namesById)));
    }

    private static AuditLogEntryDto ToDto(AuditLogEntry entry, IReadOnlyDictionary<Guid, string> namesById) => new(
        entry.Id,
        entry.UserId,
        entry.UserId.HasValue && namesById.TryGetValue(entry.UserId.Value, out var name) ? name : null,
        entry.EventType,
        entry.Detail,
        entry.IpAddress,
        entry.CreatedAtUtc);
}
