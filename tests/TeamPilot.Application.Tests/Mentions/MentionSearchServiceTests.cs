using Moq;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Git;
using TeamPilot.Application.Mentions;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Tickets;
using TeamPilot.Application.Users;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using Xunit;

namespace TeamPilot.Application.Tests.Mentions;

public class MentionSearchServiceTests
{
    private readonly Mock<ITicketRepository> _ticketRepository = new();
    private readonly Mock<IProjectRepository> _projectRepository = new();
    private readonly Mock<IGitService> _gitService = new();
    private readonly Mock<IProjectAccessGuard> _projectAccessGuard = new();
    private readonly Mock<ICurrentUserContext> _currentUser = new();
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly MentionSearchService _sut;
    private readonly Guid _userId = Guid.NewGuid();

    public MentionSearchServiceTests()
    {
        _currentUser.Setup(u => u.UserId).Returns(_userId);
        _sut = new MentionSearchService(
            _ticketRepository.Object,
            _projectRepository.Object,
            _gitService.Object,
            _projectAccessGuard.Object,
            _currentUser.Object,
            _userRepository.Object);
    }

    [Fact]
    public async Task SearchTicketsAsync_AdminUser_SearchesWithNoProjectRestriction()
    {
        _currentUser.Setup(u => u.IsInRole(UserRole.Admin)).Returns(true);
        _ticketRepository
            .Setup(r => r.SearchAsync("bug", null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await _sut.SearchTicketsAsync("bug");

        _ticketRepository.Verify(r => r.SearchAsync("bug", null, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        _userRepository.Verify(r => r.GetAssignedProjectIdsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchTicketsAsync_NonAdminUser_ScopedToAssignedProjectsOnly()
    {
        var assignedProjectIds = new[] { Guid.NewGuid() };
        _currentUser.Setup(u => u.IsInRole(UserRole.Admin)).Returns(false);
        _userRepository
            .Setup(r => r.GetAssignedProjectIdsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignedProjectIds);
        _ticketRepository
            .Setup(r => r.SearchAsync("bug", assignedProjectIds, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await _sut.SearchTicketsAsync("bug");

        _ticketRepository.Verify(r => r.SearchAsync("bug", assignedProjectIds, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a")]
    public async Task SearchTicketsAsync_QueryTooShort_ReturnsEmptyWithoutQueryingRepository(string query)
    {
        var results = await _sut.SearchTicketsAsync(query);

        Assert.Empty(results);
        _ticketRepository.Verify(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchTicketsAsync_MapsProjectNameFromBulkLookup()
    {
        var project = Project.Create("Billing", "desc", "https://github.com/org/billing.git", "token");
        var ticket = Ticket.Create(project.Id, null, "Fix invoice bug", "desc", "criteria");

        _currentUser.Setup(u => u.IsInRole(UserRole.Admin)).Returns(true);
        _ticketRepository
            .Setup(r => r.SearchAsync("bug", null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([ticket]);
        _projectRepository
            .Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([project]);

        var results = await _sut.SearchTicketsAsync("bug");

        var result = Assert.Single(results);
        Assert.Equal(ticket.Id, result.Id);
        Assert.Equal(project.Id, result.ProjectId);
        Assert.Equal("Billing", result.ProjectName);
    }

    [Fact]
    public async Task SearchProjectsAsync_AdminUser_SearchesWithNoProjectRestriction()
    {
        _currentUser.Setup(u => u.IsInRole(UserRole.Admin)).Returns(true);
        _projectRepository
            .Setup(r => r.SearchAsync("billing", null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await _sut.SearchProjectsAsync("billing");

        _projectRepository.Verify(r => r.SearchAsync("billing", null, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchProjectsAsync_NonAdminUser_ScopedToAssignedProjectsOnly()
    {
        var assignedProjectIds = new[] { Guid.NewGuid() };
        _currentUser.Setup(u => u.IsInRole(UserRole.Admin)).Returns(false);
        _userRepository
            .Setup(r => r.GetAssignedProjectIdsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignedProjectIds);
        _projectRepository
            .Setup(r => r.SearchAsync("billing", assignedProjectIds, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await _sut.SearchProjectsAsync("billing");

        _projectRepository.Verify(r => r.SearchAsync("billing", assignedProjectIds, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchProjectsAsync_QueryTooShort_ReturnsEmptyWithoutQueryingRepository()
    {
        var results = await _sut.SearchProjectsAsync("a");

        Assert.Empty(results);
        _projectRepository.Verify(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchFilesAsync_QueryTooShort_ReturnsEmptyWithoutCheckingAccessOrSearching()
    {
        var results = await _sut.SearchFilesAsync(Guid.NewGuid(), "a");

        Assert.Empty(results);
        _projectAccessGuard.Verify(g => g.EnsureAccessAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _gitService.Verify(g => g.SearchFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchFilesAsync_CallerLacksAccess_PropagatesForbiddenException()
    {
        var project = Project.Create("Billing", "desc", "https://github.com/org/billing.git", "token");
        _projectAccessGuard
            .Setup(g => g.EnsureAccessAsync(project.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("no access"));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.SearchFilesAsync(project.Id, "foo"));
        _gitService.Verify(g => g.SearchFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchFilesAsync_ProjectNotReady_ReturnsEmptyWithoutSearching()
    {
        var project = Project.Create("Billing", "desc", "https://github.com/org/billing.git", "token");
        _projectRepository.Setup(r => r.GetByIdAsync(project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var results = await _sut.SearchFilesAsync(project.Id, "foo");

        Assert.Empty(results);
        _gitService.Verify(g => g.SearchFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchFilesAsync_ProjectReady_SearchesItsRepositoryAndMapsPaths()
    {
        var project = Project.Create("Billing", "desc", "https://github.com/org/billing.git", "token");
        project.MarkCloned("/sandboxes/billing");
        _projectRepository.Setup(r => r.GetByIdAsync(project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(project);
        _gitService
            .Setup(g => g.SearchFilesAsync(project.RepositoryPath, "invoice", null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["src/invoice/service.ts"]);

        var results = await _sut.SearchFilesAsync(project.Id, "invoice");

        var result = Assert.Single(results);
        Assert.Equal("src/invoice/service.ts", result.Path);
    }
}
