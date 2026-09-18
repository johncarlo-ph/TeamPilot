using TeamPilot.Application.Sprints.Dtos;

namespace TeamPilot.Application.Dashboard.Dtos;

public sealed record DashboardSummaryDto(
    int ActiveSprintCount,
    TicketStatusCountsDto TicketStatusCounts,
    NeedsAttentionDto NeedsAttention);
