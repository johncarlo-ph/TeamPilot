using Moq;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Git;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Sprints;
using TeamPilot.Application.Sprints.Dtos;
using TeamPilot.Application.Sprints.Validators;
using TeamPilot.Application.Tickets;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using Xunit;

namespace TeamPilot.Application.Tests.Sprints;

public class SprintServiceTests
{
    private readonly Mock<ISprintRepository> _sprintRepository = new();
    private readonly Mock<IProjectRepository> _projectRepository = new();
    private readonly Mock<ITicketRepository> _ticketRepository = new();
    private readonly Mock<IProjectAccessGuard> _projectAccessGuard = new();
    private readonly Mock<IGitService> _gitService = new();
    private readonly Mock<IGitCredentialProtector> _credentialProtector = new();
    private readonly Mock<IAuditLogger> _auditLogger = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly SprintService _sut;
    private readonly Project _project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token");

    public SprintServiceTests()
    {
        _credentialProtector
            .Setup(p => p.Unprotect(It.IsAny<string>()))
            .Returns((string encrypted) => encrypted.StartsWith("encrypted:", StringComparison.Ordinal) ? encrypted["encrypted:".Length..] : encrypted);
        _gitService
            .Setup(g => g.RemoteBranchExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _projectRepository.Setup(r => r.GetByIdAsync(_project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_project);
        _ticketRepository
            .Setup(r => r.GetStatusCountsBySprintAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyDictionary<TicketStatus, int>>());

        Sprint? added = null;
        _sprintRepository
            .Setup(r => r.AddAsync(It.IsAny<Sprint>(), It.IsAny<CancellationToken>()))
            .Callback<Sprint, CancellationToken>((sprint, _) => added = sprint)
            .Returns(Task.CompletedTask);
        _sprintRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid id, CancellationToken _) => Task.FromResult(added?.Id == id ? added : null));

        _sut = new SprintService(
            _sprintRepository.Object,
            _projectRepository.Object,
            _ticketRepository.Object,
            _projectAccessGuard.Object,
            _gitService.Object,
            _credentialProtector.Object,
            _auditLogger.Object,
            _unitOfWork.Object,
            new CreateSprintRequestValidator(),
            new UpdateSprintRequestValidator());
    }

    [Fact]
    public async Task CreateAsync_WithValidRequest_CreatesSprintUnderTheProject()
    {
        var request = new CreateSprintRequest("Sprint 1", "develop");

        var result = await _sut.CreateAsync(_project.Id, request);

        Assert.Equal("Sprint 1", result.Name);
        Assert.Equal("develop", result.BaseBranch);
        Assert.Equal(_project.Id, result.ProjectId);
        _sprintRepository.Verify(r => r.AddAsync(It.IsAny<Sprint>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithNoBaseBranchGiven_ChecksTheDefaultMainBranchOnTheRemote()
    {
        var request = new CreateSprintRequest("Sprint 1", null);

        var result = await _sut.CreateAsync(_project.Id, request);

        Assert.Equal("main", result.BaseBranch);
        _gitService.Verify(g => g.RemoteBranchExistsAsync(_project.RemoteUrl, It.IsAny<string>(), "main", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WhenBaseBranchDoesNotExistOnTheRemote_ThrowsAndDoesNotCreateTheSprint()
    {
        var request = new CreateSprintRequest("Sprint 1", "no-such-branch");
        _gitService
            .Setup(g => g.RemoteBranchExistsAsync(_project.RemoteUrl, It.IsAny<string>(), "no-such-branch", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<GitOperationException>(() => _sut.CreateAsync(_project.Id, request));

        _sprintRepository.Verify(r => r.AddAsync(It.IsAny<Sprint>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithValidRequest_ReturnsZeroedTicketStatusCounts()
    {
        var request = new CreateSprintRequest("Sprint 1", "main");

        var result = await _sut.CreateAsync(_project.Id, request);

        Assert.Equal(0, result.TicketStatusCounts.ToDo);
        Assert.Equal(0, result.TicketStatusCounts.InProgress);
        Assert.Equal(0, result.TicketStatusCounts.Blocked);
        Assert.Equal(0, result.TicketStatusCounts.ForReview);
        Assert.Equal(0, result.TicketStatusCounts.Done);
    }

    [Fact]
    public async Task GetByIdAsync_WhenSprintDoesNotExist_ThrowsNotFoundException()
    {
        _sprintRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Sprint?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetByIdAsync_WhenSprintIsRemoved_ThrowsNotFoundException()
    {
        var sprint = Sprint.Create(_project.Id, "Sprint 1", "main");
        sprint.Remove();
        _sprintRepository.Setup(r => r.GetByIdAsync(sprint.Id, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetByIdAsync(sprint.Id));
    }

    [Fact]
    public async Task ListAsync_PopulatesTicketStatusCountsFromTheRepository()
    {
        var sprint = Sprint.Create(_project.Id, "Sprint 1", "main");
        _sprintRepository.Setup(r => r.ListAsync(_project.Id, It.IsAny<CancellationToken>())).ReturnsAsync([sprint]);
        _ticketRepository
            .Setup(r => r.GetStatusCountsBySprintAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyDictionary<TicketStatus, int>>
            {
                [sprint.Id] = new Dictionary<TicketStatus, int>
                {
                    [TicketStatus.ToDo] = 2,
                    [TicketStatus.Blocked] = 1,
                },
            });

        var result = await _sut.ListAsync(_project.Id);

        Assert.Equal(2, result[0].TicketStatusCounts.ToDo);
        Assert.Equal(1, result[0].TicketStatusCounts.Blocked);
        Assert.Equal(0, result[0].TicketStatusCounts.InProgress);
    }

    [Fact]
    public async Task ListAsync_ExcludesRemovedSprints()
    {
        var visible = Sprint.Create(_project.Id, "Visible", "main");
        var removed = Sprint.Create(_project.Id, "Removed", "main");
        removed.Remove();
        _sprintRepository.Setup(r => r.ListAsync(_project.Id, It.IsAny<CancellationToken>())).ReturnsAsync([visible, removed]);

        var result = await _sut.ListAsync(_project.Id);

        Assert.Single(result);
        Assert.Equal(visible.Id, result[0].Id);
    }

    [Fact]
    public async Task UpdateAsync_WhenSprintExists_UpdatesDetails()
    {
        var sprint = Sprint.Create(_project.Id, "Sprint 1", "main");
        _sprintRepository.Setup(r => r.GetByIdAsync(sprint.Id, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);

        var request = new UpdateSprintRequest("Sprint 1 Renamed", "develop");
        var result = await _sut.UpdateAsync(sprint.Id, request);

        Assert.Equal("Sprint 1 Renamed", result.Name);
        Assert.Equal("develop", result.BaseBranch);
    }

    [Fact]
    public async Task UpdateAsync_WhenBaseBranchDoesNotExistOnTheRemote_ThrowsAndDoesNotUpdateTheSprint()
    {
        var sprint = Sprint.Create(_project.Id, "Sprint 1", "main");
        _sprintRepository.Setup(r => r.GetByIdAsync(sprint.Id, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);
        _gitService
            .Setup(g => g.RemoteBranchExistsAsync(_project.RemoteUrl, It.IsAny<string>(), "no-such-branch", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var request = new UpdateSprintRequest("Sprint 1 Renamed", "no-such-branch");

        await Assert.ThrowsAsync<GitOperationException>(() => _sut.UpdateAsync(sprint.Id, request));

        Assert.Equal("Sprint 1", sprint.Name);
        Assert.Equal("main", sprint.BaseBranch);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveAsync_WhenSprintExistsWithNoActiveTickets_MarksItRemovedAndSavesChanges()
    {
        var sprint = Sprint.Create(_project.Id, "Sprint 1", "main");
        _sprintRepository.Setup(r => r.GetByIdAsync(sprint.Id, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);
        _ticketRepository.Setup(r => r.CountByStatusBySprintAsync(sprint.Id, TicketStatus.InProgress, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _ticketRepository.Setup(r => r.CountByStatusBySprintAsync(sprint.Id, TicketStatus.ForReview, It.IsAny<CancellationToken>())).ReturnsAsync(0);

        await _sut.RemoveAsync(sprint.Id);

        Assert.True(sprint.IsRemoved);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(2, 3)]
    public async Task RemoveAsync_WhenSprintHasInProgressOrForReviewTickets_ThrowsAndDoesNotRemove(int inProgressCount, int forReviewCount)
    {
        var sprint = Sprint.Create(_project.Id, "Sprint 1", "main");
        _sprintRepository.Setup(r => r.GetByIdAsync(sprint.Id, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);
        _ticketRepository.Setup(r => r.CountByStatusBySprintAsync(sprint.Id, TicketStatus.InProgress, It.IsAny<CancellationToken>())).ReturnsAsync(inProgressCount);
        _ticketRepository.Setup(r => r.CountByStatusBySprintAsync(sprint.Id, TicketStatus.ForReview, It.IsAny<CancellationToken>())).ReturnsAsync(forReviewCount);

        await Assert.ThrowsAsync<SprintHasActiveTicketsException>(() => _sut.RemoveAsync(sprint.Id));

        Assert.False(sprint.IsRemoved);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveAsync_WhenSprintDoesNotExist_ThrowsNotFoundException()
    {
        _sprintRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Sprint?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.RemoveAsync(Guid.NewGuid()));
    }
}
