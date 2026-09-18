using Moq;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Dashboard;
using TeamPilot.Application.Dashboard.Rows;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Users;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using Xunit;

namespace TeamPilot.Application.Tests.Dashboard;

public class DashboardServiceTests
{
    private readonly Mock<IDashboardRepository> _dashboardRepository = new();
    private readonly Mock<IProjectRepository> _projectRepository = new();
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<ICurrentUserContext> _currentUser = new();
    private readonly DashboardService _sut;

    private readonly Project _accessibleProject = Project.Create("Accessible", "desc", "https://github.com/org/accessible.git", "token");
    private readonly Project _inaccessibleProject = Project.Create("Inaccessible", "desc", "https://github.com/org/other.git", "token");

    public DashboardServiceTests()
    {
        _projectRepository
            .Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([_accessibleProject, _inaccessibleProject]);

        _dashboardRepository
            .Setup(r => r.CountActiveSprintsAsync(It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _dashboardRepository
            .Setup(r => r.GetGlobalTicketStatusCountsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<TicketStatus, int>());
        _dashboardRepository
            .Setup(r => r.GetAttentionTicketsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _dashboardRepository
            .Setup(r => r.GetAtRiskSprintsAsync(It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _sut = new DashboardService(
            _dashboardRepository.Object,
            _projectRepository.Object,
            _userRepository.Object,
            _currentUser.Object);
    }

    [Fact]
    public async Task GetSummaryAsync_ForAdmin_QueriesAcrossEveryNonRemovedProject()
    {
        _currentUser.Setup(c => c.IsInRole(UserRole.Admin)).Returns(true);

        await _sut.GetSummaryAsync();

        _dashboardRepository.Verify(r => r.GetGlobalTicketStatusCountsAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(_accessibleProject.Id) && ids.Contains(_inaccessibleProject.Id)),
            It.IsAny<CancellationToken>()));
        _userRepository.Verify(r => r.GetAssignedProjectIdsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetSummaryAsync_ForNonAdmin_ScopesQueriesToAssignedProjectsOnly()
    {
        var userId = Guid.NewGuid();
        _currentUser.Setup(c => c.IsInRole(UserRole.Admin)).Returns(false);
        _currentUser.Setup(c => c.UserId).Returns(userId);
        _userRepository
            .Setup(r => r.GetAssignedProjectIdsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_accessibleProject.Id]);

        await _sut.GetSummaryAsync();

        _dashboardRepository.Verify(r => r.GetGlobalTicketStatusCountsAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(_accessibleProject.Id)),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task GetSummaryAsync_MissingStatusCounts_DefaultToZero()
    {
        _currentUser.Setup(c => c.IsInRole(UserRole.Admin)).Returns(true);
        _dashboardRepository
            .Setup(r => r.GetGlobalTicketStatusCountsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<TicketStatus, int> { [TicketStatus.Blocked] = 3 });

        var result = await _sut.GetSummaryAsync();

        Assert.Equal(3, result.TicketStatusCounts.Blocked);
        Assert.Equal(0, result.TicketStatusCounts.ToDo);
        Assert.Equal(0, result.TicketStatusCounts.InProgress);
        Assert.Equal(0, result.TicketStatusCounts.ForReview);
        Assert.Equal(0, result.TicketStatusCounts.Done);
    }

    [Fact]
    public async Task GetSummaryAsync_SplitsAttentionTicketsByStatus()
    {
        _currentUser.Setup(c => c.IsInRole(UserRole.Admin)).Returns(true);
        var blockedTicketId = Guid.NewGuid();
        var forReviewTicketId = Guid.NewGuid();
        _dashboardRepository
            .Setup(r => r.GetAttentionTicketsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new AttentionTicketRow(blockedTicketId, "Blocked ticket", TicketStatus.Blocked, _accessibleProject.Id, _accessibleProject.Name, null, null),
                new AttentionTicketRow(forReviewTicketId, "Review ticket", TicketStatus.ForReview, _accessibleProject.Id, _accessibleProject.Name, null, null),
            ]);

        var result = await _sut.GetSummaryAsync();

        Assert.Single(result.NeedsAttention.BlockedTickets);
        Assert.Equal(blockedTicketId, result.NeedsAttention.BlockedTickets[0].Id);
        Assert.Single(result.NeedsAttention.TicketsForReview);
        Assert.Equal(forReviewTicketId, result.NeedsAttention.TicketsForReview[0].Id);
    }
}
