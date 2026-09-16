using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly Mock<IBackgroundTaskRunner> _backgroundTaskRunner = new();
    private readonly Mock<IProjectEventBroadcaster> _eventBroadcaster = new();
    private readonly ProjectService _sut;

    public ProjectServiceTests()
    {
        _credentialProtector.Setup(p => p.Protect(It.IsAny<string>())).Returns((string plaintext) => $"encrypted:{plaintext}");
        _credentialProtector
            .Setup(p => p.Unprotect(It.IsAny<string>()))
            .Returns((string encrypted) => encrypted.StartsWith("encrypted:", StringComparison.Ordinal) ? encrypted["encrypted:".Length..] : encrypted);
        _gitService
            .Setup(g => g.CloneAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<GitCloneProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid projectId, string _, string _, IProgress<GitCloneProgress>? _, CancellationToken _) => $"C:/git-sandboxes/{projectId}");
        _gitService
            .Setup(g => g.RemoteBranchExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _ticketRepository
            .Setup(r => r.GetStatusCountsByProjectAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyDictionary<TicketStatus, int>>());

        // CreateAsync persists the project immediately, then dispatches its clone detached (see
        // ProjectService.StartCloneDetached) instead of awaiting it inline. AddAsync's callback
        // captures the added project so the background reload (GetByIdAsync) can find it, the
        // same way it would find a real just-persisted row.
        Project? addedProject = null;
        _projectRepository
            .Setup(r => r.AddAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()))
            .Callback<Project, CancellationToken>((project, _) => addedProject = project)
            .Returns(Task.CompletedTask);
        _projectRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid id, CancellationToken _) => Task.FromResult(addedProject?.Id == id ? addedProject : null));

        // Mirrors OrchestrationServiceTests' own IBackgroundTaskRunner fake: Run executes its
        // work item synchronously against a minimal IServiceProvider wired to this test's own
        // mocks, keeping every CreateAsync test deterministic instead of racing real threading.
        var backgroundServiceProvider = new FakeServiceProvider(new Dictionary<Type, object>
        {
            [typeof(IProjectRepository)] = _projectRepository.Object,
            [typeof(IGitService)] = _gitService.Object,
            [typeof(IWorkflowService)] = _workflowService.Object,
            [typeof(IAgentService)] = _agentService.Object,
            [typeof(IAuditLogger)] = _auditLogger.Object,
            [typeof(IUnitOfWork)] = _unitOfWork.Object,
            [typeof(IProjectEventBroadcaster)] = _eventBroadcaster.Object,
            [typeof(ILogger<ProjectService>)] = NullLogger<ProjectService>.Instance,
        });

        _backgroundTaskRunner
            .Setup(r => r.Run(It.IsAny<Func<IServiceProvider, CancellationToken, Task>>()))
            .Callback<Func<IServiceProvider, CancellationToken, Task>>(
                work => work(backgroundServiceProvider, CancellationToken.None).GetAwaiter().GetResult());

        _sut = new ProjectService(
            _projectRepository.Object,
            _ticketRepository.Object,
            _userRepository.Object,
            _currentUser.Object,
            _projectAccessGuard.Object,
            _gitService.Object,
            _credentialProtector.Object,
            _auditLogger.Object,
            _unitOfWork.Object,
            _backgroundTaskRunner.Object,
            new CreateProjectRequestValidator(),
            new UpdateProjectRequestValidator());
    }

    [Fact]
    public async Task CreateAsync_DispatchesTheCloneDetachedAndReturnsImmediatelyWithCloningStatus()
    {
        // Overrides the shared constructor setup (which runs the background work synchronously)
        // so this test can observe exactly what CreateAsync hands back to its caller before any
        // detached work runs - what a real caller actually sees.
        _backgroundTaskRunner.Reset();
        var request = new CreateProjectRequest("TeamPilot", "desc", "https://github.com/org/teampilot.git", "pat-123", "main");

        var result = await _sut.CreateAsync(request);

        Assert.Equal(ProjectStatus.Cloning, result.Status);
        Assert.Null(result.CloneFailureReason);
        _backgroundTaskRunner.Verify(r => r.Run(It.IsAny<Func<IServiceProvider, CancellationToken, Task>>()), Times.Once);
        _gitService.Verify(
            g => g.CloneAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<GitCloneProgress>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithValidRequest_ClonesAndAddsProjectAndSavesChanges()
    {
        var request = new CreateProjectRequest("TeamPilot", "AI ticketing system", "https://github.com/org/teampilot.git", "pat-123", "develop");

        var result = await _sut.CreateAsync(request);

        Assert.Equal("TeamPilot", result.Name);
        Assert.Equal("https://github.com/org/teampilot.git", result.RemoteUrl);
        Assert.Equal("develop", result.BaseBranch);
        // This test's fake IBackgroundTaskRunner runs the detached clone synchronously, so by
        // the time CreateAsync returns the clone has already "succeeded" - unlike production,
        // where the caller gets Status: Cloning back immediately (see the test above).
        Assert.Equal(ProjectStatus.Ready, result.Status);
        _gitService.Verify(
            g => g.CloneAsync(It.IsAny<Guid>(), request.RemoteUrl, request.AccessToken, It.IsAny<IProgress<GitCloneProgress>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _projectRepository.Verify(r => r.AddAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()), Times.Once);
        // Once right after AddAsync (persisting the Cloning row), once more after the detached
        // clone succeeds (persisting MarkCloned) - see ProjectService.CreateAsync/StartCloneDetached.
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
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
    public async Task CreateAsync_WhenCloneFails_PersistsTheProjectAsFailedInsteadOfThrowing()
    {
        var request = new CreateProjectRequest("TeamPilot", "desc", "https://github.com/org/teampilot.git", "bad-token", "main");
        _gitService
            .Setup(g => g.CloneAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<GitCloneProgress>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GitOperationException("Could not clone."));

        // CreateAsync itself never throws - the clone runs detached (see
        // ProjectService.StartCloneDetached), so a failure is recorded on the project (visible
        // as Status: Failed) instead of propagating to the request that created it.
        var result = await _sut.CreateAsync(request);

        Assert.Equal(ProjectStatus.Failed, result.Status);
        Assert.Equal("Could not clone.", result.CloneFailureReason);
        _projectRepository.Verify(r => r.AddAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        _workflowService.Verify(s => s.EnsureDefaultWorkflowAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WhenBaseBranchDoesNotExistOnTheRemote_ThrowsAndDoesNotCreateTheProject()
    {
        var request = new CreateProjectRequest("TeamPilot", "desc", "https://github.com/org/teampilot.git", "pat-123", "no-such-branch");
        _gitService
            .Setup(g => g.RemoteBranchExistsAsync(request.RemoteUrl, request.AccessToken, "no-such-branch", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<GitOperationException>(() => _sut.CreateAsync(request));

        _projectRepository.Verify(r => r.AddAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()), Times.Never);
        _gitService.Verify(
            g => g.CloneAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<GitCloneProgress>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithNoBaseBranchGiven_ChecksTheDefaultMainBranchOnTheRemote()
    {
        var request = new CreateProjectRequest("TeamPilot", "desc", "https://github.com/org/teampilot.git", "pat-123", null);

        var result = await _sut.CreateAsync(request);

        Assert.Equal("main", result.BaseBranch);
        _gitService.Verify(g => g.RemoteBranchExistsAsync(request.RemoteUrl, request.AccessToken, "main", It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task UpdateAsync_WhenBaseBranchDoesNotExistOnTheRemote_ThrowsAndDoesNotUpdateTheProject()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main");
        _projectRepository.Setup(r => r.GetByIdAsync(project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(project);
        _gitService
            .Setup(g => g.RemoteBranchExistsAsync(project.RemoteUrl, It.IsAny<string>(), "no-such-branch", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var request = new UpdateProjectRequest("TeamPilot Renamed", "desc", null, "no-such-branch");

        await Assert.ThrowsAsync<GitOperationException>(() => _sut.UpdateAsync(project.Id, request));

        Assert.Equal("TeamPilot", project.Name);
        Assert.Equal("main", project.BaseBranch);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_WithNoAccessToken_ChecksTheRemoteBranchUsingTheExistingDecryptedToken()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted:pat-123", "main");
        _projectRepository.Setup(r => r.GetByIdAsync(project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var request = new UpdateProjectRequest("TeamPilot", "desc", null, "develop");
        await _sut.UpdateAsync(project.Id, request);

        _gitService.Verify(g => g.RemoteBranchExistsAsync(project.RemoteUrl, "pat-123", "develop", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WithNewAccessToken_ChecksTheRemoteBranchUsingTheNewToken()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted:old-pat", "main");
        _projectRepository.Setup(r => r.GetByIdAsync(project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var request = new UpdateProjectRequest("TeamPilot", "desc", "new-pat", "develop");
        await _sut.UpdateAsync(project.Id, request);

        _gitService.Verify(g => g.RemoteBranchExistsAsync(project.RemoteUrl, "new-pat", "develop", It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task ListAsync_ExcludesRemovedProjects()
    {
        var visible = Project.Create("Visible", "desc", "https://github.com/org/visible.git", "encrypted-token", "main");
        var removed = Project.Create("Removed", "desc", "https://github.com/org/removed.git", "encrypted-token", "main");
        removed.Remove();
        _projectRepository.Setup(r => r.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([visible, removed]);
        _currentUser.Setup(c => c.IsInRole(UserRole.Admin)).Returns(true);

        var result = await _sut.ListAsync();

        Assert.Single(result);
        Assert.Equal(visible.Id, result[0].Id);
    }

    [Fact]
    public async Task GetByIdAsync_WhenProjectIsRemoved_ThrowsNotFoundException()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main");
        project.Remove();
        _projectRepository.Setup(r => r.GetByIdAsync(project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetByIdAsync(project.Id));
    }

    [Fact]
    public async Task RemoveAsync_WhenProjectExistsWithNoActiveTickets_MarksItRemovedAndSavesChanges()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main");
        _projectRepository.Setup(r => r.GetByIdAsync(project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(project);
        _ticketRepository.Setup(r => r.CountByStatusAsync(project.Id, TicketStatus.InProgress, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _ticketRepository.Setup(r => r.CountByStatusAsync(project.Id, TicketStatus.ForReview, It.IsAny<CancellationToken>())).ReturnsAsync(0);

        await _sut.RemoveAsync(project.Id);

        Assert.True(project.IsRemoved);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(2, 3)]
    public async Task RemoveAsync_WhenProjectHasInProgressOrForReviewTickets_ThrowsAndDoesNotRemove(int inProgressCount, int forReviewCount)
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main");
        _projectRepository.Setup(r => r.GetByIdAsync(project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(project);
        _ticketRepository.Setup(r => r.CountByStatusAsync(project.Id, TicketStatus.InProgress, It.IsAny<CancellationToken>())).ReturnsAsync(inProgressCount);
        _ticketRepository.Setup(r => r.CountByStatusAsync(project.Id, TicketStatus.ForReview, It.IsAny<CancellationToken>())).ReturnsAsync(forReviewCount);

        await Assert.ThrowsAsync<ProjectHasActiveTicketsException>(() => _sut.RemoveAsync(project.Id));

        Assert.False(project.IsRemoved);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveAsync_WhenProjectDoesNotExist_ThrowsNotFoundException()
    {
        _projectRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Project?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.RemoveAsync(Guid.NewGuid()));
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

    /// <summary>
    /// Minimal <see cref="IServiceProvider"/> standing in for the fresh DI scope
    /// <see cref="IBackgroundTaskRunner"/> would normally create - just enough for the delegate
    /// ProjectService hands to <c>Run</c> to resolve its dependencies via
    /// <c>GetRequiredService</c>.
    /// </summary>
    private sealed class FakeServiceProvider(IReadOnlyDictionary<Type, object> services) : IServiceProvider
    {
        public object? GetService(Type serviceType) => services.GetValueOrDefault(serviceType);
    }
}
