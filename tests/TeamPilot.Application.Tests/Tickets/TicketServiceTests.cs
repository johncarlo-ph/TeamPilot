using Moq;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Git;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Sprints;
using TeamPilot.Application.Tickets;
using TeamPilot.Application.Tickets.Dtos;
using TeamPilot.Application.Tickets.Validators;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using TeamPilot.Domain.Exceptions;
using Xunit;

namespace TeamPilot.Application.Tests.Tickets;

public class TicketServiceTests
{
    private readonly Mock<ITicketRepository> _ticketRepository = new();
    private readonly Mock<IProjectRepository> _projectRepository = new();
    private readonly Mock<ISprintRepository> _sprintRepository = new();
    private readonly Mock<IGitService> _gitService = new();
    private readonly Mock<IGitCredentialProtector> _credentialProtector = new();
    private readonly Mock<IProjectAccessGuard> _projectAccessGuard = new();
    private readonly Mock<IAuditLogger> _auditLogger = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IPipelineRunTracker> _pipelineRunTracker = new();
    private readonly Mock<IProjectEventBroadcaster> _eventBroadcaster = new();
    private readonly TicketService _sut;
    private readonly Project _project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token");
    private readonly Sprint _sprint;

    public TicketServiceTests()
    {
        // Ready by default - CreateAsync now rejects a project that's still cloning (or failed
        // to). Tests exercising that guard set up their own not-Ready project instead.
        _project.MarkCloned("C:/git-sandboxes/" + _project.Id);
        _sprint = Sprint.Create(_project.Id, "Sprint 1", "develop");

        _projectRepository.Setup(r => r.GetByIdAsync(_project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_project);
        _sprintRepository.Setup(r => r.GetByIdAsync(_sprint.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_sprint);
        _credentialProtector.Setup(p => p.Unprotect(_project.EncryptedAccessToken)).Returns("plaintext-token");

        _sut = new TicketService(
            _ticketRepository.Object,
            _projectRepository.Object,
            _sprintRepository.Object,
            _gitService.Object,
            _credentialProtector.Object,
            _projectAccessGuard.Object,
            _auditLogger.Object,
            _unitOfWork.Object,
            _pipelineRunTracker.Object,
            _eventBroadcaster.Object,
            new CreateTicketRequestValidator(),
            new CreateBranchRequestValidator(),
            new CancelTicketRequestValidator());
    }

    [Fact]
    public async Task CreateAsync_WithValidRequest_AddsTicketAndSavesChanges()
    {
        var request = new CreateTicketRequest("Implement login", "Add OAuth login flow", "User can log in via OAuth.");

        var result = await _sut.CreateAsync(_sprint.Id, request);

        Assert.Equal("Implement login", result.Title);
        Assert.Equal(_project.Id, result.ProjectId);
        Assert.Equal(TicketStatus.ToDo, result.Status);
        _ticketRepository.Verify(r => r.AddAsync(It.IsAny<Ticket>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _eventBroadcaster.Verify(b => b.Publish(_project.Id, It.Is<ProjectEvent>(e => e.Type == ProjectEventTypes.TicketChanged)), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithEmptyTitle_ThrowsValidationException()
    {
        var request = new CreateTicketRequest(string.Empty, null, "Acceptance criteria");

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateAsync(_sprint.Id, request));
    }

    [Fact]
    public async Task CreateAsync_WithEmptyAcceptanceCriteria_ThrowsValidationException()
    {
        var request = new CreateTicketRequest("Implement login", "Add OAuth login flow", string.Empty);

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateAsync(_sprint.Id, request));
    }

    [Fact]
    public async Task CreateAsync_WhenProjectIsStillCloning_ThrowsProjectNotReadyExceptionAndDoesNotAddTicket()
    {
        var cloningProject = Project.Create("Cloning Project", "desc", "https://github.com/org/cloning.git", "encrypted-token");
        var cloningSprint = Sprint.Create(cloningProject.Id, "Sprint 1", "main");
        _projectRepository.Setup(r => r.GetByIdAsync(cloningProject.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cloningProject);
        _sprintRepository.Setup(r => r.GetByIdAsync(cloningSprint.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cloningSprint);

        var request = new CreateTicketRequest("Implement login", "Add OAuth login flow", "User can log in via OAuth.");

        await Assert.ThrowsAsync<ProjectNotReadyException>(() => _sut.CreateAsync(cloningSprint.Id, request));
        _ticketRepository.Verify(r => r.AddAsync(It.IsAny<Ticket>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WhenProjectFailedToClone_ThrowsProjectNotReadyException()
    {
        var failedProject = Project.Create("Failed Project", "desc", "https://github.com/org/failed.git", "encrypted-token");
        failedProject.MarkCloneFailed("Could not clone.");
        var failedSprint = Sprint.Create(failedProject.Id, "Sprint 1", "main");
        _projectRepository.Setup(r => r.GetByIdAsync(failedProject.Id, It.IsAny<CancellationToken>())).ReturnsAsync(failedProject);
        _sprintRepository.Setup(r => r.GetByIdAsync(failedSprint.Id, It.IsAny<CancellationToken>())).ReturnsAsync(failedSprint);

        var request = new CreateTicketRequest("Implement login", "Add OAuth login flow", "User can log in via OAuth.");

        await Assert.ThrowsAsync<ProjectNotReadyException>(() => _sut.CreateAsync(failedSprint.Id, request));
    }

    [Fact]
    public async Task CreateAsync_WhenProjectDoesNotExist_ThrowsNotFoundException()
    {
        _projectRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Project?)null);
        var request = new CreateTicketRequest("Implement login", null, "User can log in via OAuth.");

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.CreateAsync(Guid.NewGuid(), request));
    }

    [Fact]
    public async Task GetByIdAsync_WhenTicketDoesNotExist_ThrowsNotFoundException()
    {
        _ticketRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Ticket?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task MoveToReviewAsync_WhenTicketIsInProgress_SetsStatusToForReview()
    {
        var ticket = Ticket.Create(_project.Id, _sprint.Id, "Fix bug", "desc", "Acceptance criteria");
        ticket.AssignAgent(Agent.Create(_project.Id, "Coder", AgentRole.Coding));

        _ticketRepository
            .Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        var result = await _sut.MoveToReviewAsync(ticket.Id);

        Assert.Equal(TicketStatus.ForReview, result.Status);
    }

    [Fact]
    public async Task DeleteBranchAsync_WhenTicketIsCancelled_DeletesBranchAndClearsBranchName()
    {
        var ticket = Ticket.Create(_project.Id, _sprint.Id, "Fix bug", "desc", "Acceptance criteria");
        ticket.LinkBranch("feature/fix-bug");
        ticket.Cancel("No longer needed");

        _ticketRepository
            .Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        var result = await _sut.DeleteBranchAsync(ticket.Id);

        Assert.Null(result.BranchName);
        _gitService.Verify(
            g => g.DeleteBranchAsync(_project.RepositoryPath, "feature/fix-bug", _sprint.BaseBranch, "plaintext-token", It.IsAny<CancellationToken>()),
            Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteBranchAsync_WhenTicketIsNotCancelled_ThrowsInvalidTicketStateTransitionExceptionWithoutTouchingGit()
    {
        var ticket = Ticket.Create(_project.Id, _sprint.Id, "Fix bug", "desc", "Acceptance criteria");
        ticket.LinkBranch("feature/fix-bug");

        _ticketRepository
            .Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        await Assert.ThrowsAsync<InvalidTicketStateTransitionException>(() => _sut.DeleteBranchAsync(ticket.Id));

        _gitService.Verify(
            g => g.DeleteBranchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteBranchAsync_WhenTicketHasNoLinkedBranch_ThrowsInvalidOperationException()
    {
        var ticket = Ticket.Create(_project.Id, _sprint.Id, "Fix bug", "desc", "Acceptance criteria");
        ticket.Cancel("No longer needed");

        _ticketRepository
            .Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.DeleteBranchAsync(ticket.Id));
    }

    [Fact]
    public async Task LinkBranchAsync_WithValidBranchName_EnsuresBranchAndLinksTicket()
    {
        var ticket = Ticket.Create(_project.Id, _sprint.Id, "Fix bug", "desc", "Acceptance criteria");

        _ticketRepository
            .Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        var result = await _sut.LinkBranchAsync(ticket.Id, "feature/fix-bug");

        Assert.Equal("feature/fix-bug", result.BranchName);
        _gitService.Verify(g => g.FetchAsync(_project.RepositoryPath, "plaintext-token", It.IsAny<CancellationToken>()), Times.Once);
        _gitService.Verify(g => g.EnsureBranchAsync(_project.RepositoryPath, "feature/fix-bug", _sprint.BaseBranch, It.IsAny<CancellationToken>()), Times.Once);
        _gitService.Verify(g => g.PushAsync(_project.RepositoryPath, "feature/fix-bug", "plaintext-token", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LinkBranchAsync_WhenBranchAlreadyLinkedToAnotherTicket_ThrowsBranchAlreadyLinkedException()
    {
        var ticket = Ticket.Create(_project.Id, _sprint.Id, "Fix bug", "desc", "Acceptance criteria");
        var otherTicket = Ticket.Create(_project.Id, _sprint.Id, "Add feature", "desc", "Acceptance criteria");
        otherTicket.LinkBranch("feature/shared");

        _ticketRepository
            .Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);
        _ticketRepository
            .Setup(r => r.GetByBranchNameAsync(_project.Id, "feature/shared", It.IsAny<CancellationToken>()))
            .ReturnsAsync(otherTicket);

        await Assert.ThrowsAsync<BranchAlreadyLinkedException>(() => _sut.LinkBranchAsync(ticket.Id, "feature/shared"));
        _gitService.Verify(g => g.EnsureBranchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LinkBranchAsync_WhenTicketIsCancelled_ThrowsInvalidTicketStateTransitionException()
    {
        var ticket = Ticket.Create(_project.Id, _sprint.Id, "Fix bug", "desc", "Acceptance criteria");
        ticket.Cancel("No longer needed");

        _ticketRepository
            .Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        await Assert.ThrowsAsync<InvalidTicketStateTransitionException>(() => _sut.LinkBranchAsync(ticket.Id, "feature/fix-bug"));
    }

    [Fact]
    public async Task LinkBranchAsync_WhenReLinkingSameTicketToItsOwnBranch_Succeeds()
    {
        var ticket = Ticket.Create(_project.Id, _sprint.Id, "Fix bug", "desc", "Acceptance criteria");
        ticket.LinkBranch("feature/fix-bug");

        _ticketRepository
            .Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);
        _ticketRepository
            .Setup(r => r.GetByBranchNameAsync(_project.Id, "feature/fix-bug", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        var result = await _sut.LinkBranchAsync(ticket.Id, "feature/fix-bug");

        Assert.Equal("feature/fix-bug", result.BranchName);
    }

    [Fact]
    public async Task BranchExistsAsync_FetchesRemoteThenReturnsGitServiceResult()
    {
        var ticket = Ticket.Create(_project.Id, _sprint.Id, "Fix bug", "desc", "Acceptance criteria");

        _ticketRepository
            .Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);
        _gitService
            .Setup(g => g.BranchExistsAsync(_project.RepositoryPath, "feature/fix-bug", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.BranchExistsAsync(ticket.Id, "feature/fix-bug");

        Assert.True(result);
        _gitService.Verify(g => g.FetchAsync(_project.RepositoryPath, "plaintext-token", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BranchExistsAsync_WhenTicketDoesNotExist_ThrowsNotFoundException()
    {
        _ticketRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Ticket?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.BranchExistsAsync(Guid.NewGuid(), "feature/fix-bug"));
    }

    [Fact]
    public async Task CancelAsync_WhenTicketIsInProgress_SetsStatusToCancelledAndRecordsReason()
    {
        var ticket = Ticket.Create(_project.Id, _sprint.Id, "Fix bug", "desc", "Acceptance criteria");
        ticket.AssignAgent(Agent.Create(_project.Id, "Coder", AgentRole.Coding));

        _ticketRepository
            .Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        var result = await _sut.CancelAsync(ticket.Id, new CancelTicketRequest("No longer needed"));

        Assert.Equal(TicketStatus.Cancelled, result.Status);
        Assert.Equal("No longer needed", result.CancellationReason);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelAsync_WhenTicketHasLinkedBranch_DeletesBranchAndClearsBranchName()
    {
        var ticket = Ticket.Create(_project.Id, _sprint.Id, "Fix bug", "desc", "Acceptance criteria");
        ticket.LinkBranch("feature/fix-bug");

        _ticketRepository
            .Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        var result = await _sut.CancelAsync(ticket.Id, new CancelTicketRequest("No longer needed"));

        Assert.Equal(TicketStatus.Cancelled, result.Status);
        Assert.Null(result.BranchName);
        _gitService.Verify(
            g => g.DeleteBranchAsync(_project.RepositoryPath, "feature/fix-bug", _sprint.BaseBranch, "plaintext-token", It.IsAny<CancellationToken>()),
            Times.Once);
        // Twice: once to persist Cancelled before the branch delete (so a concurrent pipeline
        // run's fresh status check - see OrchestrationService.RunCodingStageAsync/LinkBranchAsync
        // - is guaranteed to see it), once after the delete completes.
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CancelAsync_WhenTicketHasNoLinkedBranch_DoesNotCallGitService()
    {
        var ticket = Ticket.Create(_project.Id, _sprint.Id, "Fix bug", "desc", "Acceptance criteria");

        _ticketRepository
            .Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        await _sut.CancelAsync(ticket.Id, new CancelTicketRequest(null));

        _gitService.Verify(
            g => g.DeleteBranchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CancelAsync_WhenTicketIsDone_ThrowsInvalidTicketStateTransitionException()
    {
        var ticket = Ticket.Create(_project.Id, _sprint.Id, "Fix bug", "desc", "Acceptance criteria");
        ticket.AssignAgent(Agent.Create(_project.Id, "Coder", AgentRole.Coding));
        ticket.MoveToReview();
        ticket.Approve();

        _ticketRepository
            .Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        await Assert.ThrowsAsync<InvalidTicketStateTransitionException>(
            () => _sut.CancelAsync(ticket.Id, new CancelTicketRequest(null)));
    }

    [Fact]
    public async Task CancelAsync_WhenTicketDoesNotExist_ThrowsNotFoundException()
    {
        _ticketRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Ticket?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.CancelAsync(Guid.NewGuid(), new CancelTicketRequest(null)));
    }
}
