using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TeamPilot.Application.Approval;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Git;
using TeamPilot.Application.Orchestration;
using TeamPilot.Application.Orchestration.Dtos;
using TeamPilot.Application.Pipelines;
using TeamPilot.Application.Pipelines.Dtos;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Reviews.Dtos;
using TeamPilot.Application.Reviews.Validators;
using TeamPilot.Application.TicketQuestions;
using TeamPilot.Application.Tickets;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using Xunit;

namespace TeamPilot.Application.Tests.Approval;

public class ApprovalGateServiceTests
{
    private readonly Mock<ITicketRepository> _ticketRepository = new();
    private readonly Mock<IProjectRepository> _projectRepository = new();
    private readonly Mock<IGitService> _gitService = new();
    private readonly Mock<IGitCredentialProtector> _credentialProtector = new();
    private readonly Mock<IOrchestrationService> _orchestrationService = new();
    private readonly Mock<IPipelineService> _pipelineService = new();
    private readonly Mock<IProjectAccessGuard> _projectAccessGuard = new();
    private readonly Mock<ICurrentUserContext> _currentUser = new();
    private readonly Mock<IAuditLogger> _auditLogger = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ITicketQuestionRepository> _ticketQuestionRepository = new();
    private readonly Mock<IBackgroundTaskRunner> _backgroundTaskRunner = new();
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

        _pipelineService
            .Setup(p => p.TriggerAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid projectId, Guid? ticketId, string reason, CancellationToken _) =>
                new PipelineRunDto(Guid.NewGuid(), projectId, ticketId, PipelineRunStatus.Queued, reason, null, null, null, DateTime.UtcNow));

        // Default happy path: the live merge attempt succeeds with nothing left unresolved.
        // Tests exercising a failed merge (e.g. a conflict the live check still finds) override
        // this per-test.
        _gitService
            .Setup(g => g.MergeWithResolutionsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitMergeResolutionResult(true, Array.Empty<string>()));

        // The RequestChanges path now kicks the pipeline re-run off via IBackgroundTaskRunner
        // instead of awaiting it directly. Rather than mock that abstraction away entirely, this
        // makes Run execute its work item synchronously - inline, right here - against a minimal
        // IServiceProvider wired to this test's own mocks. That keeps every RequestChanges test
        // deterministic (no real threading to race against) while still exercising the exact
        // delegate ApprovalGateService hands it, background dependency resolution included.
        var backgroundServiceProvider = new FakeServiceProvider(new Dictionary<Type, object>
        {
            [typeof(IOrchestrationService)] = _orchestrationService.Object,
            [typeof(ITicketRepository)] = _ticketRepository.Object,
            [typeof(ITicketQuestionRepository)] = _ticketQuestionRepository.Object,
            [typeof(IAuditLogger)] = _auditLogger.Object,
            [typeof(IUnitOfWork)] = _unitOfWork.Object,
            [typeof(ILogger<ApprovalGateService>)] = NullLogger<ApprovalGateService>.Instance,
        });

        _backgroundTaskRunner
            .Setup(r => r.Run(It.IsAny<Func<IServiceProvider, CancellationToken, Task>>()))
            .Callback<Func<IServiceProvider, CancellationToken, Task>>(
                work => work(backgroundServiceProvider, CancellationToken.None).GetAwaiter().GetResult());

        _sut = new ApprovalGateService(
            _ticketRepository.Object,
            _projectRepository.Object,
            _gitService.Object,
            _credentialProtector.Object,
            _pipelineService.Object,
            _projectAccessGuard.Object,
            _currentUser.Object,
            _auditLogger.Object,
            _unitOfWork.Object,
            new SubmitReviewRequestValidator(),
            _backgroundTaskRunner.Object,
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
        _pipelineService.Verify(
            p => p.TriggerAsync(_project.Id, ticket.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()),
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
    public async Task SubmitReviewAsync_WhenDecisionIsRequestChanges_MovesTicketBackToInProgressAndRunsThePipelineInTheBackground()
    {
        var ticket = CreateTicketInReview("feature/add-feature");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        var request = new SubmitReviewRequest("Bob", ReviewDecision.RequestChanges, "Needs work");

        var result = await _sut.SubmitReviewAsync(ticket.Id, request);

        // The mocked IOrchestrationService doesn't actually move the ticket to ForReview the way
        // a real re-run would - this just confirms RequestChanges itself lands on InProgress and
        // the returned DTO reflects that immediately, without waiting on the pipeline re-run.
        Assert.Equal(TicketStatus.InProgress, result.Status);
        _gitService.Verify(
            g => g.MergeWithResolutionsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _pipelineService.Verify(
            p => p.TriggerAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _backgroundTaskRunner.Verify(r => r.Run(It.IsAny<Func<IServiceProvider, CancellationToken, Task>>()), Times.Once);
        _orchestrationService.Verify(o => o.RunPipelineAsync(ticket.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitReviewAsync_WhenBackgroundPipelineRerunThrowsUnexpectedly_BlocksTicketWithARetryableFailureQuestion()
    {
        var ticket = CreateTicketInReview("feature/add-feature");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        _orchestrationService
            .Setup(o => o.RunPipelineAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Project was deleted mid-run."));

        var request = new SubmitReviewRequest("Bob", ReviewDecision.RequestChanges, "Needs work");

        // The background-runner mock executes the failing re-run (and its failure handler)
        // synchronously, so by the time this returns the ticket already reflects the outcome -
        // it must not be left silently stuck In Progress with no visible sign anything failed.
        await _sut.SubmitReviewAsync(ticket.Id, request);

        Assert.Equal(TicketStatus.Blocked, ticket.Status);
        _ticketQuestionRepository.Verify(
            r => r.AddAsync(
                It.Is<TicketQuestion>(q => q.TicketId == ticket.Id && q.Kind == TicketQuestionKind.Failure),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    [Fact]
    public async Task SubmitReviewAsync_WhenBackgroundPipelineRerunThrowsAfterTheTicketWasAlreadyBlocked_DoesNotDoubleBlock()
    {
        // RunPipelineAsync itself already handles known Git/LLM failures by blocking the ticket
        // and returning normally rather than throwing - so an exception reaching the background
        // failure handler with the ticket already out of InProgress (e.g. blocked by a
        // near-simultaneous request, or by RunPipelineAsync's own handling in a path this mock
        // doesn't model) should be a no-op rather than throwing from a second Block() call.
        var ticket = CreateTicketInReview("feature/add-feature");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        _orchestrationService
            .Setup(o => o.RunPipelineAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .Callback(() => ticket.Block())
            .ThrowsAsync(new InvalidOperationException("boom"));

        var request = new SubmitReviewRequest("Bob", ReviewDecision.RequestChanges, "Needs work");

        await _sut.SubmitReviewAsync(ticket.Id, request);

        Assert.Equal(TicketStatus.Blocked, ticket.Status);
        _ticketQuestionRepository.Verify(r => r.AddAsync(It.IsAny<TicketQuestion>(), It.IsAny<CancellationToken>()), Times.Never);
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
        _pipelineService.Verify(
            p => p.TriggerAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
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

    /// <summary>
    /// Minimal <see cref="IServiceProvider"/> standing in for the fresh DI scope
    /// <see cref="IBackgroundTaskRunner"/> would normally create - just enough for the delegate
    /// ApprovalGateService hands to <c>Run</c> to resolve its dependencies via
    /// <c>GetRequiredService</c>.
    /// </summary>
    private sealed class FakeServiceProvider(IReadOnlyDictionary<Type, object> services) : IServiceProvider
    {
        public object? GetService(Type serviceType) => services.GetValueOrDefault(serviceType);
    }
}
