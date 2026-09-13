using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using TeamPilot.Domain.Exceptions;
using Xunit;

namespace TeamPilot.Domain.Tests;

public class TicketQuestionTests
{
    private static readonly Guid TicketId = Guid.NewGuid();
    private static readonly Guid AgentId = Guid.NewGuid();

    [Fact]
    public void CreateQuestion_WithEmptyTicketId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => TicketQuestion.CreateQuestion(Guid.Empty, AgentId, "What timeout should I use?"));
    }

    [Fact]
    public void CreateQuestion_WithEmptyQuestionText_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => TicketQuestion.CreateQuestion(TicketId, AgentId, "   "));
    }

    [Fact]
    public void CreateQuestion_WithValidArguments_SetsFieldsAndPendingStatus()
    {
        var question = TicketQuestion.CreateQuestion(TicketId, AgentId, "What timeout should I use?");

        Assert.Equal(TicketId, question.TicketId);
        Assert.Equal(AgentId, question.AgentId);
        Assert.Equal(TicketQuestionKind.Question, question.Kind);
        Assert.Equal("What timeout should I use?", question.Prompt);
        Assert.Equal(TicketQuestionStatus.Pending, question.Status);
        Assert.False(question.Consumed);
    }

    [Fact]
    public void CreateFailure_WithNullAgentId_Succeeds()
    {
        var question = TicketQuestion.CreateFailure(TicketId, null, "Git push failed.");

        Assert.Null(question.AgentId);
        Assert.Equal(TicketQuestionKind.Failure, question.Kind);
    }

    [Fact]
    public void CreateFailure_WithEmptyMessage_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => TicketQuestion.CreateFailure(TicketId, AgentId, ""));
    }

    [Fact]
    public void Answer_OnAQuestion_SetsAnswerAndStatus()
    {
        var question = TicketQuestion.CreateQuestion(TicketId, AgentId, "What timeout should I use?");

        question.Answer("30 seconds", "Alice");

        Assert.Equal(TicketQuestionStatus.Answered, question.Status);
        Assert.Equal("30 seconds", question.AnswerText);
        Assert.Equal("Alice", question.AnsweredBy);
        Assert.NotNull(question.AnsweredAtUtc);
    }

    [Fact]
    public void Answer_WhenAlreadyAnswered_ThrowsInvalidTicketQuestionStateException()
    {
        var question = TicketQuestion.CreateQuestion(TicketId, AgentId, "What timeout should I use?");
        question.Answer("30 seconds", "Alice");

        Assert.Throws<InvalidTicketQuestionStateException>(() => question.Answer("60 seconds", "Bob"));
    }

    [Fact]
    public void Answer_OnAFailure_ThrowsInvalidOperationException()
    {
        var question = TicketQuestion.CreateFailure(TicketId, AgentId, "Git push failed.");

        Assert.Throws<InvalidOperationException>(() => question.Answer("Retrying", "Alice"));
    }

    [Fact]
    public void MarkConsumed_SetsConsumedTrue()
    {
        var question = TicketQuestion.CreateQuestion(TicketId, AgentId, "What timeout should I use?");

        question.MarkConsumed();

        Assert.True(question.Consumed);
    }
}
