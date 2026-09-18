using Microsoft.AspNetCore.Mvc;
using TeamPilot.Application.Dashboard;
using TeamPilot.Application.Dashboard.Dtos;

namespace TeamPilot.API.Controllers;

[ApiController]
public class DashboardController(IDashboardService dashboardService) : ControllerBase
{
    [HttpGet("api/dashboard/summary")]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary(CancellationToken cancellationToken)
    {
        var summary = await dashboardService.GetSummaryAsync(cancellationToken);
        return Ok(summary);
    }
}
