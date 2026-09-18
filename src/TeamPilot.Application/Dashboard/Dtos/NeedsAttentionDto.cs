namespace TeamPilot.Application.Dashboard.Dtos;

public sealed record NeedsAttentionDto(
    IReadOnlyList<AttentionTicketDto> BlockedTickets,
    IReadOnlyList<AttentionTicketDto> TicketsForReview,
    IReadOnlyList<AtRiskSprintDto> SprintsAtRisk);
