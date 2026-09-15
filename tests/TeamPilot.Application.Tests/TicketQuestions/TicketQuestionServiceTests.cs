using Moq;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Orchestration;
using TeamPilot.Application.TicketQuestions;
using TeamPilot.Application.TicketQuestions.Dtos;
using TeamPilot.Application.TicketQuestions.Validators;
using TeamPilot.Application.Tickets;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using TeamPilot.Domain.Exceptions;
using Xunit;

namespace TeamPilot.Application.Tests.TicketQuestions;

public class TicketQuestionServiceTests
{
    private readonly Mock<ITicketRepository> _ticketRepository = new();
    private readonly Mock<ITicketQuestionRepository> _ticketQuestionRepository = new();
    private readonly Mock<IOrchestrationService> _orchestrationService = new();
    private readonly Mock<IProjectAccessGuard> _projectAccessGuard = new();
    private readonly Mock<ICurrentUserContext> _currentUser = new();
    private readonly Mock<IAuditLogger> _auditLogger = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IPipelineRunTracker> _pipelineRunTracker = new();
    private readonly Mock<IProjectEventBroadcaster> _eventBroadcaster = new();
    private readonly TicketQuestionService _sut;
    private readonly Guid _projectId = Guid.NewGuid();

    public TicketQuestionServiceTests()
    {
        _currentUser.Setup(c => c.Name).Returns("Alice");

        _sut = new TicketQuestionService(
            _ticketRepository.Object,
            _ticketQuestionRepository.Object,
            _orchestrationService.Object,
            _projectAccessGuard.Object,
            _currentUser.Object,
            _auditLogger.Object,
            _unitOfWork.Object,
            _pipelineRunTracker.Object,
            _eventBroadcaster.Object,
            new AnswerTicketQuestionRequestValidator());
    }

    private Ticket CreateBlockedTicket()
    {
        var ticket = Ticket.Create(_projectId, "Build feature", "desc");
        ticket.AssignAgent(Agent.Create(_projectId, "Coder", AgentRole.Coding));
        ticket.Block();
        return ticket;
    }

    [Fact]
    public async Task ListByTicketAsync_ChecksProjectAccessAndReturnsTheRepositoryListing()
    {
        var ticket = CreateBlockedTicket();
        var questions = new List<TicketQuestion> { TicketQuestion.CreateQuestion(ticket.Id, Guid.NewGuid(), "Which provider?") };
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _ticketQuestionRepository.Setup(r => r.ListByTicketAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(questions);

        var result = await _sut.ListByTicketAsync(ticket.Id);

        Assert.Same(questions, result);
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
    public async Task ListByTicketAsync_WhenCallerLacksProjectAccess_ThrowsForbiddenExceptionAndNeverQueriesQuestions()
    {
        var ticket = CreateBlockedTicket();
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _projectAccessGuard.Setup(g => g.EnsureAccessAsync(ticket.ProjectId, It.IsAny<CancellationToken>())).ThrowsAsync(new ForbiddenException("No access."));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ListByTicketAsync(ticket.Id));
        _ticketQuestionRepository.Verify(r => r.ListByTicketAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnswerAsync_WithAPendingQuestion_AnswersUnblocksAndKicksOffThePipelineDetached()
    {
        var ticket = CreateBlockedTicket();
        var question = TicketQuestion.CreateQuestion(ticket.Id, Guid.NewGuid(), "Which provider?");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _ticketQuestionRepository.Setup(r => r.GetByIdAsync(question.Id, It.IsAny<CancellationToken>())).ReturnsAsync(question);
        // The mocked IOrchestrationService.RunPipelineDetached doesn't actually touch the real
        // tracker, so this stands in for what it would report once really invoked.
        _pipelineRunTracker.Setup(t => t.IsRunning(ticket.Id)).Returns(true);

        var result = await _sut.AnswerAsync(ticket.Id, question.Id, new AnswerTicketQuestionRequest("Use Google."));

        Assert.Equal(TicketQuestionStatus.Answered, question.Status);
        Assert.Equal("Use Google.", question.AnswerText);
        Assert.Equal("Alice", question.AnsweredBy);
        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        // The pipeline re-run is kicked off detached (see IOrchestrationService.RunPipelineDetached)
        // instead of awaited, so the returned DTO reflects the ticket right after Unblock() -
        // still InProgress, not whatever the eventual re-run settles on - but PipelineRunning
        // already reports the run as in flight.
        Assert.Equal(TicketStatus.InProgress, result.Status);
        Assert.True(result.PipelineRunning);
        _orchestrationService.Verify(o => o.RunPipelineDetached(ticket.ProjectId, ticket.Id), Times.Once);
        _eventBroadcaster.Verify(b => b.Publish(ticket.ProjectId, It.Is<ProjectEvent>(e => e.Type == ProjectEventTypes.TicketChanged && e.TicketId == ticket.Id)), Times.Once);
        _eventBroadcaster.Verify(b => b.Publish(ticket.ProjectId, It.Is<ProjectEvent>(e => e.Type == ProjectEventTypes.TicketQuestionChanged && e.TicketId == ticket.Id)), Times.Once);
    }

    [Fact]
    public async Task AnswerAsync_WithAPendingDecision_AnswersUnblocksAndKicksOffThePipelineDetached()
    {
        var ticket = CreateBlockedTicket();
        var decision = TicketQuestion.CreateDecision(ticket.Id, Guid.NewGuid(), "Should this proceed given the conflicting ticket?");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _ticketQuestionRepository.Setup(r => r.GetByIdAsync(decision.Id, It.IsAny<CancellationToken>())).ReturnsAsync(decision);

        var result = await _sut.AnswerAsync(ticket.Id, decision.Id, new AnswerTicketQuestionRequest("Continue anyway."));

        Assert.Equal(TicketQuestionStatus.Answered, decision.Status);
        Assert.Equal("Continue anyway.", decision.AnswerText);
        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.Equal(TicketStatus.InProgress, result.Status);
        _orchestrationService.Verify(o => o.RunPipelineDetached(ticket.ProjectId, ticket.Id), Times.Once);
    }

    [Fact]
    public async Task AnswerAsync_WhenQuestionBelongsToADifferentTicket_ThrowsNotFoundException()
    {
        var ticket = CreateBlockedTicket();
        var otherTicketQuestion = TicketQuestion.CreateQuestion(Guid.NewGuid(), Guid.NewGuid(), "Which provider?");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _ticketQuestionRepository.Setup(r => r.GetByIdAsync(otherTicketQuestion.Id, It.IsAny<CancellationToken>())).ReturnsAsync(otherTicketQuestion);

        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.AnswerAsync(ticket.Id, otherTicketQuestion.Id, new AnswerTicketQuestionRequest("Use Google.")));
    }

    [Fact]
    public async Task AnswerAsync_WhenAlreadyAnswered_ThrowsInvalidTicketQuestionStateException()
    {
        var ticket = CreateBlockedTicket();
        var question = TicketQuestion.CreateQuestion(ticket.Id, Guid.NewGuid(), "Which provider?");
        question.Answer("Use Google.", "Bob");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _ticketQuestionRepository.Setup(r => r.GetByIdAsync(question.Id, It.IsAny<CancellationToken>())).ReturnsAsync(question);

        await Assert.ThrowsAsync<InvalidTicketQuestionStateException>(
            () => _sut.AnswerAsync(ticket.Id, question.Id, new AnswerTicketQuestionRequest("Use Azure.")));
    }

    [Fact]
    public async Task RetryAsync_WhenMostRecentQuestionIsAFailure_UnblocksAndKicksOffThePipelineDetached()
    {
        var ticket = CreateBlockedTicket();
        var failure = TicketQuestion.CreateFailure(ticket.Id, Guid.NewGuid(), "Git push failed.");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _ticketQuestionRepository.Setup(r => r.GetMostRecentAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(failure);
        _pipelineRunTracker.Setup(t => t.IsRunning(ticket.Id)).Returns(true);

        var result = await _sut.RetryAsync(ticket.Id);

        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.True(result.PipelineRunning);
        _orchestrationService.Verify(o => o.RunPipelineDetached(ticket.ProjectId, ticket.Id), Times.Once);
        _eventBroadcaster.Verify(b => b.Publish(ticket.ProjectId, It.Is<ProjectEvent>(e => e.Type == ProjectEventTypes.TicketChanged && e.TicketId == ticket.Id)), Times.Once);
    }

    [Fact]
    public async Task RetryAsync_WhenMostRecentQuestionIsAClarifyingQuestion_ThrowsInvalidOperationException()
    {
        var ticket = CreateBlockedTicket();
        var question = TicketQuestion.CreateQuestion(ticket.Id, Guid.NewGuid(), "Which provider?");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _ticketQuestionRepository.Setup(r => r.GetMostRecentAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(question);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.RetryAsync(ticket.Id));
        Assert.Equal(TicketStatus.Blocked, ticket.Status);
    }

    [Fact]
    public async Task RetryAsync_WhenMostRecentQuestionIsADecision_ThrowsInvalidOperationException()
    {
        var ticket = CreateBlockedTicket();
        var decision = TicketQuestion.CreateDecision(ticket.Id, Guid.NewGuid(), "Should this proceed given the conflicting ticket?");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _ticketQuestionRepository.Setup(r => r.GetMostRecentAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(decision);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.RetryAsync(ticket.Id));
        Assert.Equal(TicketStatus.Blocked, ticket.Status);
    }
}
