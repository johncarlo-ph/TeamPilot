using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamPilot.Application.AuditLog;
using TeamPilot.Application.AuditLog.Dtos;
using TeamPilot.Domain.Entities;

namespace TeamPilot.API.Controllers;

[ApiController]
[Route("api/audit-log")]
[Authorize(Roles = "Admin")]
public class AuditLogController(IAuditLogRepository auditLogRepository) : ControllerBase
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
        return Ok(entries.Select(ToDto));
    }

    private static AuditLogEntryDto ToDto(AuditLogEntry entry) => new(
        entry.Id,
        entry.UserId,
        entry.EventType,
        entry.Detail,
        entry.IpAddress,
        entry.CreatedAtUtc);
}
