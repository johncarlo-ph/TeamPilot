using TeamPilot.Application.Dashboard.Dtos;

namespace TeamPilot.Application.Dashboard;

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
}
