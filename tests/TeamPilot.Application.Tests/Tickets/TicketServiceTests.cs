using Moq;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Git;
using TeamPilot.Application.Projects;
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
    private readonly Mock<IGitService> _gitService = new();
    private readonly Mock<IGitCredentialProtector> _credentialProtector = new();
    private readonly Mock<IProjectAccessGuard> _projectAccessGuard = new();
    private readonly Mock<IAuditLogger> _auditLogger = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IPipelineRunTracker> _pipelineRunTracker = new();
    private readonly Mock<IProjectEventBroadcaster> _eventBroadcaster = new();
    private readonly TicketService _sut;
    private readonly Project _project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "develop");

    public TicketServiceTests()
    {
        _projectRepository.Setup(r => r.GetByIdAsync(_project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_project);
        _credentialProtector.Setup(p => p.Unprotect(_project.EncryptedAccessToken)).Returns("plaintext-token");

        _sut = new TicketService(
            _ticketRepository.Object,
            _projectRepository.Object,
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
        var request = new CreateTicketRequest("Implement login", "Add OAuth login flow");

        var result = await _sut.CreateAsync(_project.Id, request);

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
        var request = new CreateTicketRequest(string.Empty, null);

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateAsync(_project.Id, request));
    }

    [Fact]
    public async Task CreateAsync_WhenProjectDoesNotExist_ThrowsNotFoundException()
    {
        _projectRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Project?)null);
        var request = new CreateTicketRequest("Implement login", null);

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
        var ticket = Ticket.Create(_project.Id, "Fix bug", "desc");
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
        var ticket = Ticket.Create(_project.Id, "Fix bug", "desc");
        ticket.LinkBranch("feature/fix-bug");
        ticket.Cancel("No longer needed");

        _ticketRepository
            .Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        var result = await _sut.DeleteBranchAsync(ticket.Id);

        Assert.Null(result.BranchName);
        _gitService.Verify(
            g => g.DeleteBranchAsync(_project.RepositoryPath, "feature/fix-bug", _project.BaseBranch, "plaintext-token", It.IsAny<CancellationToken>()),
            Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteBranchAsync_WhenTicketIsNotCancelled_ThrowsInvalidTicketStateTransitionExceptionWithoutTouchingGit()
    {
        var ticket = Ticket.Create(_project.Id, "Fix bug", "desc");
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
        var ticket = Ticket.Create(_project.Id, "Fix bug", "desc");
        ticket.Cancel("No longer needed");

        _ticketRepository
            .Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.DeleteBranchAsync(ticket.Id));
    }

    [Fact]
    public async Task LinkBranchAsync_WithValidBranchName_EnsuresBranchAndLinksTicket()
    {
        var ticket = Ticket.Create(_project.Id, "Fix bug", "desc");

        _ticketRepository
            .Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        var result = await _sut.LinkBranchAsync(ticket.Id, "feature/fix-bug");

        Assert.Equal("feature/fix-bug", result.BranchName);
        _gitService.Verify(g => g.FetchAsync(_project.RepositoryPath, "plaintext-token", It.IsAny<CancellationToken>()), Times.Once);
        _gitService.Verify(g => g.EnsureBranchAsync(_project.RepositoryPath, "feature/fix-bug", _project.BaseBranch, It.IsAny<CancellationToken>()), Times.Once);
        _gitService.Verify(g => g.PushAsync(_project.RepositoryPath, "feature/fix-bug", "plaintext-token", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LinkBranchAsync_WhenBranchAlreadyLinkedToAnotherTicket_ThrowsBranchAlreadyLinkedException()
    {
        var ticket = Ticket.Create(_project.Id, "Fix bug", "desc");
        var otherTicket = Ticket.Create(_project.Id, "Add feature", "desc");
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
        var ticket = Ticket.Create(_project.Id, "Fix bug", "desc");
        ticket.Cancel("No longer needed");

        _ticketRepository
            .Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        await Assert.ThrowsAsync<InvalidTicketStateTransitionException>(() => _sut.LinkBranchAsync(ticket.Id, "feature/fix-bug"));
    }

    [Fact]
    public async Task LinkBranchAsync_WhenReLinkingSameTicketToItsOwnBranch_Succeeds()
    {
        var ticket = Ticket.Create(_project.Id, "Fix bug", "desc");
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
        var ticket = Ticket.Create(_project.Id, "Fix bug", "desc");

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
        var ticket = Ticket.Create(_project.Id, "Fix bug", "desc");
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
        var ticket = Ticket.Create(_project.Id, "Fix bug", "desc");
        ticket.LinkBranch("feature/fix-bug");

        _ticketRepository
            .Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        var result = await _sut.CancelAsync(ticket.Id, new CancelTicketRequest("No longer needed"));

        Assert.Equal(TicketStatus.Cancelled, result.Status);
        Assert.Null(result.BranchName);
        _gitService.Verify(
            g => g.DeleteBranchAsync(_project.RepositoryPath, "feature/fix-bug", _project.BaseBranch, "plaintext-token", It.IsAny<CancellationToken>()),
            Times.Once);
        // Twice: once to persist Cancelled before the branch delete (so a concurrent pipeline
        // run's fresh status check - see OrchestrationService.RunCodingStageAsync/LinkBranchAsync
        // - is guaranteed to see it), once after the delete completes.
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CancelAsync_WhenTicketHasNoLinkedBranch_DoesNotCallGitService()
    {
        var ticket = Ticket.Create(_project.Id, "Fix bug", "desc");

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
        var ticket = Ticket.Create(_project.Id, "Fix bug", "desc");
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
