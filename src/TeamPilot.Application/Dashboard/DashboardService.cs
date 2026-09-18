using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Dashboard.Dtos;
using TeamPilot.Application.Dashboard.Rows;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Sprints.Dtos;
using TeamPilot.Application.Users;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Dashboard;

/// <summary>
/// Composes the projects landing page's dashboard summary from <see cref="IDashboardRepository"/>'s
/// cross-aggregate queries, scoped to the current user's accessible projects the same way
/// <c>ProjectService.ListAsync</c> scopes the project list itself (Admins see everything; other
/// roles only their assigned projects).
/// </summary>
public sealed class DashboardService(
    IDashboardRepository dashboardRepository,
    IProjectRepository projectRepository,
    IUserRepository userRepository,
    ICurrentUserContext currentUser) : IDashboardService
{
    /// <summary>How many days ahead of today a sprint's end date still counts as "at risk" -
    /// sprints whose end date has already passed are always included regardless of this window.</summary>
    private const int AtRiskWindowDays = 3;

    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var accessibleProjectIds = await GetAccessibleProjectIdsAsync(cancellationToken);

        var asOfUtc = DateTime.UtcNow;
        var atRiskThresholdUtc = asOfUtc.Date.AddDays(AtRiskWindowDays);

        // Run sequentially, not via Task.WhenAll - every call below shares this request's single
        // scoped DbContext (through IDashboardRepository), and DbContext isn't safe for
        // concurrent operations from multiple in-flight queries at once.
        var activeSprintCount = await dashboardRepository.CountActiveSprintsAsync(asOfUtc, accessibleProjectIds, cancellationToken);
        var statusCounts = await dashboardRepository.GetGlobalTicketStatusCountsAsync(accessibleProjectIds, cancellationToken);
        var attentionTickets = await dashboardRepository.GetAttentionTicketsAsync(accessibleProjectIds, cancellationToken);
        var atRiskSprints = await dashboardRepository.GetAtRiskSprintsAsync(atRiskThresholdUtc, accessibleProjectIds, cancellationToken);

        return new DashboardSummaryDto(
            activeSprintCount,
            ToCountsDto(statusCounts),
            new NeedsAttentionDto(
                attentionTickets.Where(t => t.Status == TicketStatus.Blocked).Select(ToAttentionTicketDto).ToList(),
                attentionTickets.Where(t => t.Status == TicketStatus.ForReview).Select(ToAttentionTicketDto).ToList(),
                atRiskSprints.Select(ToAtRiskSprintDto).ToList()));
    }

    private async Task<IReadOnlyCollection<Guid>> GetAccessibleProjectIdsAsync(CancellationToken cancellationToken)
    {
        var projects = (await projectRepository.ListAsync(cancellationToken)).Where(p => !p.IsRemoved);

        if (!currentUser.IsInRole(UserRole.Admin))
        {
            var assignedProjectIds = await userRepository.GetAssignedProjectIdsAsync(currentUser.UserId, cancellationToken);
            projects = projects.Where(p => assignedProjectIds.Contains(p.Id));
        }

        return projects.Select(p => p.Id).ToList();
    }

    private static TicketStatusCountsDto ToCountsDto(IReadOnlyDictionary<TicketStatus, int> counts) => new(
        ToDo: counts.GetValueOrDefault(TicketStatus.ToDo),
        InProgress: counts.GetValueOrDefault(TicketStatus.InProgress),
        Blocked: counts.GetValueOrDefault(TicketStatus.Blocked),
        ForReview: counts.GetValueOrDefault(TicketStatus.ForReview),
        Done: counts.GetValueOrDefault(TicketStatus.Done));

    private static AttentionTicketDto ToAttentionTicketDto(AttentionTicketRow row) => new(
        row.Id, row.Title, row.Status, row.ProjectId, row.ProjectName, row.SprintId, row.SprintName);

    private static AtRiskSprintDto ToAtRiskSprintDto(AtRiskSprintRow row) => new(
        row.Id, row.Name, row.ProjectId, row.ProjectName, row.SprintEndDate, row.UnfinishedTicketCount);
}
