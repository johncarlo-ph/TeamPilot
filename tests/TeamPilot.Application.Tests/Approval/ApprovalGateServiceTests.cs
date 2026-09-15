using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TeamPilot.Application.Approval;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Git;
using TeamPilot.Application.Orchestration;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Reviews;
using TeamPilot.Application.Reviews.Dtos;
using TeamPilot.Application.Reviews.Validators;
using TeamPilot.Application.Tickets;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using Xunit;

namespace TeamPilot.Application.Tests.Approval;

public class ApprovalGateServiceTests
{
    private readonly Mock<ITicketRepository> _ticketRepository = new();
    private readonly Mock<IProjectRepository> _projectRepository = new();
    private readonly Mock<IReviewRepository> _reviewRepository = new();
    private readonly Mock<IGitService> _gitService = new();
    private readonly Mock<IGitCredentialProtector> _credentialProtector = new();
    private readonly Mock<IOrchestrationService> _orchestrationService = new();
    private readonly Mock<IProjectAccessGuard> _projectAccessGuard = new();
    private readonly Mock<ICurrentUserContext> _currentUser = new();
    private readonly Mock<IAuditLogger> _auditLogger = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IPipelineRunTracker> _pipelineRunTracker = new();
    private readonly Mock<IProjectEventBroadcaster> _eventBroadcaster = new();
    private readonly ApprovalGateService _sut;
    private readonly Project _project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "develop");

    public ApprovalGateServiceTests()
    {
        _unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<Task> action, CancellationToken _) => action());

        _projectRepository.Setup(r => r.GetByIdAsync(_project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_project);
        _credentialProtector.Setup(p => p.Unprotect(_project.EncryptedAccessToken)).Returns("plaintext-token");

        // Default to Developer so the existing Approve-path tests exercise the happy path;
        // the Analyst-specific tests override this per-test.
        _currentUser.Setup(c => c.IsInRole(UserRole.Developer)).Returns(true);

        // Default happy path: the live merge attempt succeeds with nothing left unresolved.
        // Tests exercising a failed merge (e.g. a conflict the live check still finds) override
        // this per-test.
        _gitService
            .Setup(g => g.MergeWithResolutionsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitMergeResolutionResult(true, Array.Empty<string>()));

        _sut = new ApprovalGateService(
            _ticketRepository.Object,
            _projectRepository.Object,
            _reviewRepository.Object,
            _gitService.Object,
            _credentialProtector.Object,
            _projectAccessGuard.Object,
            _currentUser.Object,
            _auditLogger.Object,
            _unitOfWork.Object,
            new SubmitReviewRequestValidator(),
            _orchestrationService.Object,
            _pipelineRunTracker.Object,
            _eventBroadcaster.Object,
            NullLogger<ApprovalGateService>.Instance);
    }

    private Ticket CreateTicketInReview(string branchName)
    {
        var ticket = Ticket.Create(_project.Id, "Add feature", "desc");
        ticket.AssignAgent(Agent.Create(_project.Id, "Coder", AgentRole.Coding));
        ticket.LinkBranch(branchName);
        ticket.MoveToReview();
        return ticket;
    }

    [Fact]
    public async Task ListByTicketAsync_ChecksProjectAccessAndReturnsTheRepositoryListing()
    {
        var ticket = CreateTicketInReview("feature/add-feature");
        var reviews = new List<Review> { Review.Create(ticket.Id, "Bob", ReviewDecision.Approve, "Looks good") };
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _reviewRepository.Setup(r => r.ListByTicketAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(reviews);

        var result = await _sut.ListByTicketAsync(ticket.Id);

        Assert.Same(reviews, result);
        _projectAccessGuard.Verify(g => g.EnsureAccessAsync(ticket.ProjectId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListByTicketAsync_WhenTicketDoesNotExist_ThrowsNotFoundException()
    {
        var ticketId = Guid.NewGuid();
        _ticketRepository.Setup(r => r.GetByIdAsync(ticketId, It.IsAny<CancellationToken>())).ReturnsAsync((Ticket?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ListByTicketAsync(ticketId));
    }

    [Fact]
    public async Task ListByTicketAsync_WhenCallerLacksProjectAccess_ThrowsForbiddenExceptionAndNeverQueriesReviews()
    {
        var ticket = CreateTicketInReview("feature/add-feature");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _projectAccessGuard.Setup(g => g.EnsureAccessAsync(ticket.ProjectId, It.IsAny<CancellationToken>())).ThrowsAsync(new ForbiddenException("No access."));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ListByTicketAsync(ticket.Id));
        _reviewRepository.Verify(r => r.ListByTicketAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SubmitReviewAsync_WhenDecisionIsApprove_MergesBranchAndCompletesTicket()
    {
        var ticket = CreateTicketInReview("feature/add-feature");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        var request = new SubmitReviewRequest("Alice", ReviewDecision.Approve, "Looks good");

        var result = await _sut.SubmitReviewAsync(ticket.Id, request);

        Assert.Equal(TicketStatus.Done, result.Status);
        _gitService.Verify(
            g => g.FetchAsync(_project.RepositoryPath, "plaintext-token", It.IsAny<CancellationToken>()),
            Times.Once);
        _gitService.Verify(
            g => g.MergeWithResolutionsAsync(
                _project.RepositoryPath,
                "feature/add-feature",
                _project.BaseBranch,
                It.Is<IReadOnlyDictionary<string, string>>(d => d.Count == 0),
                "Alice",
                It.IsAny<CancellationToken>()),
            Times.Once);
        _gitService.Verify(
            g => g.PushAsync(_project.RepositoryPath, _project.BaseBranch, "plaintext-token", It.IsAny<CancellationToken>()),
            Times.Once);
        _orchestrationService.Verify(o => o.RunPipelineAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SubmitReviewAsync_WhenDecisionIsReject_CancelsTicketAndDeletesItsBranch()
    {
        var ticket = CreateTicketInReview("feature/add-feature");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        var request = new SubmitReviewRequest("Alice", ReviewDecision.Reject, "Wrong approach entirely");

        var result = await _sut.SubmitReviewAsync(ticket.Id, request);

        Assert.Equal(TicketStatus.Cancelled, result.Status);
        Assert.Null(ticket.BranchName);
        _gitService.Verify(
            g => g.DeleteBranchAsync(_project.RepositoryPath, "feature/add-feature", _project.BaseBranch, "plaintext-token", It.IsAny<CancellationToken>()),
            Times.Once);
        _gitService.Verify(
            g => g.MergeWithResolutionsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _orchestrationService.Verify(o => o.RunPipelineAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SubmitReviewAsync_WhenDecisionIsRejectWithNoLinkedBranch_CancelsTicketWithoutTouchingGit()
    {
        var ticket = Ticket.Create(_project.Id, "Add feature", "desc");
        ticket.AssignAgent(Agent.Create(_project.Id, "Coder", AgentRole.Coding));
        ticket.MoveToReview();
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        var request = new SubmitReviewRequest("Alice", ReviewDecision.Reject, null);

        var result = await _sut.SubmitReviewAsync(ticket.Id, request);

        Assert.Equal(TicketStatus.Cancelled, result.Status);
        _gitService.Verify(
            g => g.DeleteBranchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SubmitReviewAsync_WhenAnalystSubmitsReject_ThrowsForbiddenException()
    {
        _currentUser.Setup(c => c.IsInRole(UserRole.Developer)).Returns(false);
        _currentUser.Setup(c => c.IsInRole(UserRole.Admin)).Returns(false);

        var ticket = CreateTicketInReview("feature/add-feature");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        var request = new SubmitReviewRequest("Alice", ReviewDecision.Reject, null);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.SubmitReviewAsync(ticket.Id, request));
        _gitService.Verify(
            g => g.DeleteBranchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SubmitReviewAsync_WhenDecisionIsRequestChanges_MovesTicketBackToInProgressAndKicksOffThePipelineDetached()
    {
        var ticket = CreateTicketInReview("feature/add-feature");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        // The mocked IOrchestrationService.RunPipelineDetached doesn't actually touch the real
        // tracker, so this stands in for what it would report once really invoked.
        _pipelineRunTracker.Setup(t => t.IsRunning(ticket.Id)).Returns(true);

        var request = new SubmitReviewRequest("Bob", ReviewDecision.RequestChanges, "Needs work");

        var result = await _sut.SubmitReviewAsync(ticket.Id, request);

        // RunPipelineDetached is fire-and-forget - see OrchestrationServiceTests for its own
        // dispatch/background-failure-handling coverage. This just confirms RequestChanges
        // itself lands on InProgress and the returned DTO reflects that immediately, and that
        // the re-run was actually kicked off, without waiting on it.
        Assert.Equal(TicketStatus.InProgress, result.Status);
        Assert.True(result.PipelineRunning);
        _gitService.Verify(
            g => g.MergeWithResolutionsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _orchestrationService.Verify(o => o.RunPipelineDetached(ticket.ProjectId, ticket.Id), Times.Once);
        _eventBroadcaster.Verify(b => b.Publish(ticket.ProjectId, It.Is<ProjectEvent>(e => e.Type == ProjectEventTypes.TicketChanged && e.TicketId == ticket.Id)), Times.Once);
    }

    [Fact]
    public async Task SubmitReviewAsync_WhenApprovingWithUnresolvedConflict_ThrowsUnresolvedConflictsException()
    {
        var ticket = CreateTicketInReview("feature/add-feature");
        ticket.RaiseConflict(Conflict.Create(ticket.Id, "src/File.cs", "<<<<<<< diff"));
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        var request = new SubmitReviewRequest("Alice", ReviewDecision.Approve, "Looks good");

        await Assert.ThrowsAsync<UnresolvedConflictsException>(() => _sut.SubmitReviewAsync(ticket.Id, request));
        _gitService.Verify(
            g => g.MergeWithResolutionsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SubmitReviewAsync_WhenApprovingWithOnlyResolvedConflicts_MergesWithTheirResolvedContentAndCompletesTicket()
    {
        var ticket = CreateTicketInReview("feature/add-feature");
        var conflict = Conflict.Create(ticket.Id, "src/File.cs", "<<<<<<< diff");
        conflict.ResolveManually("final merged content", "Kept both changes", "Alice");
        ticket.RaiseConflict(conflict);
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        var request = new SubmitReviewRequest("Alice", ReviewDecision.Approve, "Looks good");

        var result = await _sut.SubmitReviewAsync(ticket.Id, request);

        Assert.Equal(TicketStatus.Done, result.Status);
        _gitService.Verify(
            g => g.MergeWithResolutionsAsync(
                _project.RepositoryPath,
                "feature/add-feature",
                _project.BaseBranch,
                It.Is<IReadOnlyDictionary<string, string>>(d => d.Count == 1 && d["src/File.cs"] == "final merged content"),
                "Alice",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SubmitReviewAsync_WhenLiveMergeStillFindsAnUnresolvedConflict_ThrowsUnresolvedConflictsExceptionAndDoesNotApprove()
    {
        // Every Conflict record on the ticket says resolved, but the live merge attempt (e.g.
        // because the base branch moved further since it was last checked) still reports a file
        // with no matching resolution - the real, authoritative check, not the ticket's own
        // possibly-stale records.
        var ticket = CreateTicketInReview("feature/add-feature");
        var conflict = Conflict.Create(ticket.Id, "src/File.cs", "<<<<<<< diff");
        conflict.ResolveManually("final merged content", "Kept both changes", "Alice");
        ticket.RaiseConflict(conflict);
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        _gitService
            .Setup(g => g.MergeWithResolutionsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitMergeResolutionResult(false, ["src/OtherFile.cs"]));

        var request = new SubmitReviewRequest("Alice", ReviewDecision.Approve, "Looks good");

        var exception = await Assert.ThrowsAsync<UnresolvedConflictsException>(() => _sut.SubmitReviewAsync(ticket.Id, request));
        Assert.Equal(["src/OtherFile.cs"], exception.FilePaths);
    }

    [Fact]
    public async Task SubmitReviewAsync_WhenApprovingWithoutLinkedBranch_ThrowsInvalidOperationException()
    {
        var ticket = Ticket.Create(_project.Id, "Add feature", "desc");
        ticket.AssignAgent(Agent.Create(_project.Id, "Coder", AgentRole.Coding));
        ticket.MoveToReview();

        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        var request = new SubmitReviewRequest("Alice", ReviewDecision.Approve, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.SubmitReviewAsync(ticket.Id, request));
    }

    [Fact]
    public async Task SubmitReviewAsync_WhenAnalystSubmitsApprove_ThrowsForbiddenException()
    {
        _currentUser.Setup(c => c.IsInRole(UserRole.Developer)).Returns(false);
        _currentUser.Setup(c => c.IsInRole(UserRole.Admin)).Returns(false);

        var ticket = CreateTicketInReview("feature/add-feature");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        var request = new SubmitReviewRequest("Alice", ReviewDecision.Approve, null);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.SubmitReviewAsync(ticket.Id, request));
        _gitService.Verify(
            g => g.MergeWithResolutionsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SubmitReviewAsync_WhenAnalystSubmitsRequestChanges_Succeeds()
    {
        _currentUser.Setup(c => c.IsInRole(UserRole.Developer)).Returns(false);
        _currentUser.Setup(c => c.IsInRole(UserRole.Admin)).Returns(false);

        var ticket = CreateTicketInReview("feature/add-feature");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        var request = new SubmitReviewRequest("Alice", ReviewDecision.RequestChanges, "Needs work");

        var result = await _sut.SubmitReviewAsync(ticket.Id, request);

        Assert.Equal(TicketStatus.InProgress, result.Status);
    }
}
