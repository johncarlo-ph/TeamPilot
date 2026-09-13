using Moq;
using TeamPilot.Application.Agents;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Git;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Projects.Dtos;
using TeamPilot.Application.Projects.Validators;
using TeamPilot.Application.Tickets;
using TeamPilot.Application.Users;
using TeamPilot.Application.Workflow;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using Xunit;

namespace TeamPilot.Application.Tests.Projects;

public class ProjectServiceTests
{
    private readonly Mock<IProjectRepository> _projectRepository = new();
    private readonly Mock<ITicketRepository> _ticketRepository = new();
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IAgentService> _agentService = new();
    private readonly Mock<IWorkflowService> _workflowService = new();
    private readonly Mock<ICurrentUserContext> _currentUser = new();
    private readonly Mock<IProjectAccessGuard> _projectAccessGuard = new();
    private readonly Mock<IGitService> _gitService = new();
    private readonly Mock<IGitCredentialProtector> _credentialProtector = new();
    private readonly Mock<IAuditLogger> _auditLogger = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly ProjectService _sut;

    public ProjectServiceTests()
    {
        _credentialProtector.Setup(p => p.Protect(It.IsAny<string>())).Returns((string plaintext) => $"encrypted:{plaintext}");
        _gitService
            .Setup(g => g.CloneAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid projectId, string _, string _, CancellationToken _) => $"C:/git-sandboxes/{projectId}");
        _ticketRepository
            .Setup(r => r.GetStatusCountsByProjectAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyDictionary<TicketStatus, int>>());

        _sut = new ProjectService(
            _projectRepository.Object,
            _ticketRepository.Object,
            _userRepository.Object,
            _agentService.Object,
            _workflowService.Object,
            _currentUser.Object,
            _projectAccessGuard.Object,
            _gitService.Object,
            _credentialProtector.Object,
            _auditLogger.Object,
            _unitOfWork.Object,
            new CreateProjectRequestValidator(),
            new UpdateProjectRequestValidator());
    }

    [Fact]
    public async Task CreateAsync_WithValidRequest_ClonesAndAddsProjectAndSavesChanges()
    {
        var request = new CreateProjectRequest("TeamPilot", "AI ticketing system", "https://github.com/org/teampilot.git", "pat-123", "develop");

        var result = await _sut.CreateAsync(request);

        Assert.Equal("TeamPilot", result.Name);
        Assert.Equal("https://github.com/org/teampilot.git", result.RemoteUrl);
        Assert.Equal("develop", result.BaseBranch);
        _gitService.Verify(g => g.CloneAsync(It.IsAny<Guid>(), request.RemoteUrl, request.AccessToken, It.IsAny<CancellationToken>()), Times.Once);
        _projectRepository.Verify(r => r.AddAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithValidRequest_ProvisionsTheDefaultWorkflow()
    {
        var request = new CreateProjectRequest("TeamPilot", "desc", "https://github.com/org/teampilot.git", "pat-123", "main");

        var result = await _sut.CreateAsync(request);

        _workflowService.Verify(s => s.EnsureDefaultWorkflowAsync(result.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithValidRequest_ProvisionsTheStandingLiveAgent()
    {
        var request = new CreateProjectRequest("TeamPilot", "desc", "https://github.com/org/teampilot.git", "pat-123", "main");

        var result = await _sut.CreateAsync(request);

        _agentService.Verify(s => s.EnsureLiveAgentAsync(result.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_EncryptsTheAccessTokenBeforeStoringIt()
    {
        var request = new CreateProjectRequest("TeamPilot", "desc", "https://github.com/org/teampilot.git", "pat-123", "main");

        Project? added = null;
        _projectRepository
            .Setup(r => r.AddAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()))
            .Callback<Project, CancellationToken>((project, _) => added = project)
            .Returns(Task.CompletedTask);

        await _sut.CreateAsync(request);

        Assert.NotNull(added);
        Assert.Equal("encrypted:pat-123", added!.EncryptedAccessToken);
    }

    [Fact]
    public async Task CreateAsync_WhenCloneFails_PropagatesAndDoesNotPersistProject()
    {
        var request = new CreateProjectRequest("TeamPilot", "desc", "https://github.com/org/teampilot.git", "bad-token", "main");
        _gitService
            .Setup(g => g.CloneAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GitOperationException("Could not clone."));

        await Assert.ThrowsAsync<GitOperationException>(() => _sut.CreateAsync(request));

        _projectRepository.Verify(r => r.AddAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithEmptyRemoteUrl_ThrowsValidationException()
    {
        var request = new CreateProjectRequest("TeamPilot", "desc", string.Empty, "pat-123", "main");

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_WithEmptyAccessToken_ThrowsValidationException()
    {
        var request = new CreateProjectRequest("TeamPilot", "desc", "https://github.com/org/teampilot.git", string.Empty, "main");

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateAsync(request));
    }

    [Fact]
    public async Task GetByIdAsync_WhenProjectDoesNotExist_ThrowsNotFoundException()
    {
        _projectRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Project?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task UpdateAsync_WhenProjectExists_UpdatesDetailsButNotRemoteUrl()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main");
        _projectRepository.Setup(r => r.GetByIdAsync(project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var request = new UpdateProjectRequest("TeamPilot Renamed", "New description", null, "develop");
        var result = await _sut.UpdateAsync(project.Id, request);

        Assert.Equal("TeamPilot Renamed", result.Name);
        Assert.Equal("develop", result.BaseBranch);
        Assert.Equal("https://github.com/org/teampilot.git", result.RemoteUrl);
    }

    [Fact]
    public async Task UpdateAsync_WithNoAccessToken_KeepsExistingToken()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main");
        _projectRepository.Setup(r => r.GetByIdAsync(project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var request = new UpdateProjectRequest("TeamPilot", "desc", null, "main");
        await _sut.UpdateAsync(project.Id, request);

        Assert.Equal("encrypted-token", project.EncryptedAccessToken);
        _credentialProtector.Verify(p => p.Protect(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_WithNewAccessToken_RotatesTheEncryptedToken()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "old-encrypted-token", "main");
        _projectRepository.Setup(r => r.GetByIdAsync(project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var request = new UpdateProjectRequest("TeamPilot", "desc", "new-pat", "main");
        await _sut.UpdateAsync(project.Id, request);

        Assert.Equal("encrypted:new-pat", project.EncryptedAccessToken);
    }

    [Fact]
    public async Task ListAsync_WhenCallerIsAdmin_ReturnsAllProjects()
    {
        var projectA = Project.Create("A", "desc", "https://github.com/org/a.git", "encrypted-token", "main");
        var projectB = Project.Create("B", "desc", "https://github.com/org/b.git", "encrypted-token", "main");
        _projectRepository.Setup(r => r.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([projectA, projectB]);
        _currentUser.Setup(c => c.IsInRole(UserRole.Admin)).Returns(true);

        var result = await _sut.ListAsync();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ListAsync_WhenCallerIsNotAdmin_ReturnsOnlyAssignedProjects()
    {
        var projectA = Project.Create("A", "desc", "https://github.com/org/a.git", "encrypted-token", "main");
        var projectB = Project.Create("B", "desc", "https://github.com/org/b.git", "encrypted-token", "main");
        _projectRepository.Setup(r => r.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([projectA, projectB]);
        _currentUser.Setup(c => c.IsInRole(UserRole.Admin)).Returns(false);
        _currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid());
        _userRepository
            .Setup(r => r.GetAssignedProjectIdsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([projectA.Id]);

        var result = await _sut.ListAsync();

        Assert.Single(result);
        Assert.Equal(projectA.Id, result[0].Id);
    }

    [Fact]
    public async Task ListAsync_PopulatesTicketStatusCountsFromTheRepository()
    {
        var projectA = Project.Create("A", "desc", "https://github.com/org/a.git", "encrypted-token", "main");
        _projectRepository.Setup(r => r.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([projectA]);
        _currentUser.Setup(c => c.IsInRole(UserRole.Admin)).Returns(true);
        _ticketRepository
            .Setup(r => r.GetStatusCountsByProjectAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyDictionary<TicketStatus, int>>
            {
                [projectA.Id] = new Dictionary<TicketStatus, int>
                {
                    [TicketStatus.ToDo] = 2,
                    [TicketStatus.Blocked] = 1,
                },
            });

        var result = await _sut.ListAsync();

        Assert.Equal(2, result[0].TicketStatusCounts.ToDo);
        Assert.Equal(1, result[0].TicketStatusCounts.Blocked);
        Assert.Equal(0, result[0].TicketStatusCounts.InProgress);
        Assert.Equal(0, result[0].TicketStatusCounts.ForReview);
        Assert.Equal(0, result[0].TicketStatusCounts.Done);
    }

    [Fact]
    public async Task CreateAsync_WithValidRequest_ReturnsZeroedTicketStatusCounts()
    {
        var request = new CreateProjectRequest("TeamPilot", "desc", "https://github.com/org/teampilot.git", "pat-123", "main");

        var result = await _sut.CreateAsync(request);

        Assert.Equal(0, result.TicketStatusCounts.ToDo);
        Assert.Equal(0, result.TicketStatusCounts.InProgress);
        Assert.Equal(0, result.TicketStatusCounts.Blocked);
        Assert.Equal(0, result.TicketStatusCounts.ForReview);
        Assert.Equal(0, result.TicketStatusCounts.Done);
    }
}
