using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Dashboard.Rows;

/// <summary>
/// Raw projection of a Blocked/ForReview ticket joined with its project and (if assigned) sprint
/// name - returned by <see cref="IDashboardRepository.GetAttentionTicketsAsync"/> and mapped into
/// <see cref="Dtos.AttentionTicketDto"/> by <see cref="DashboardService"/>.
/// </summary>
public sealed record AttentionTicketRow(
    Guid Id,
    string Title,
    TicketStatus Status,
    Guid ProjectId,
    string ProjectName,
    Guid? SprintId,
    string? SprintName);
