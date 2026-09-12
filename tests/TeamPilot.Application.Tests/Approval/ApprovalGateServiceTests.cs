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

        _sut = new ApprovalGateService(
            _ticketRepository.Object,
            _projectRepository.Object,
            _gitService.Object,
            _credentialProtector.Object,
            _orchestrationService.Object,
            _pipelineService.Object,
            _projectAccessGuard.Object,
            _currentUser.Object,
            _auditLogger.Object,
            _unitOfWork.Object,
            new SubmitReviewRequestValidator(),
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
            g => g.MergeBranchAsync(_project.RepositoryPath, "feature/add-feature", _project.BaseBranch, "Alice", It.IsAny<CancellationToken>()),
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
            g => g.MergeBranchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
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
    public async Task SubmitReviewAsync_WhenDecisionIsRequestChanges_MovesTicketBackToInProgressAndImmediatelyRunsThePipeline()
    {
        var ticket = CreateTicketInReview("feature/add-feature");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        var request = new SubmitReviewRequest("Bob", ReviewDecision.RequestChanges, "Needs work");

        var result = await _sut.SubmitReviewAsync(ticket.Id, request);

        // The mocked IOrchestrationService doesn't actually move the ticket to ForReview the way
        // a real re-run would - this just confirms RequestChanges itself lands on InProgress
        // before the (separately verified) pipeline re-run is triggered.
        Assert.Equal(TicketStatus.InProgress, result.Status);
        _gitService.Verify(
            g => g.MergeBranchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _pipelineService.Verify(
            p => p.TriggerAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _orchestrationService.Verify(o => o.RunPipelineAsync(ticket.Id, It.IsAny<CancellationToken>()), Times.Once);
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
            g => g.MergeBranchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
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
