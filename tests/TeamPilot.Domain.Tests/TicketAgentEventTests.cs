using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using Xunit;

namespace TeamPilot.Domain.Tests;

public class TicketAgentEventTests
{
    private static readonly Guid TicketId = Guid.NewGuid();
    private static readonly Guid AgentId = Guid.NewGuid();

    [Fact]
    public void CreateStarted_WithEmptyTicketId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => TicketAgentEvent.CreateStarted(Guid.Empty, AgentId, AgentRole.Research));
    }

    [Fact]
    public void CreateStarted_WithEmptyAgentId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => TicketAgentEvent.CreateStarted(TicketId, Guid.Empty, AgentRole.Research));
    }

    [Fact]
    public void CreateStarted_WithValidArguments_SetsFieldsAndNullResultAndUsage()
    {
        var agentEvent = TicketAgentEvent.CreateStarted(TicketId, AgentId, AgentRole.Research);

        Assert.Equal(TicketId, agentEvent.TicketId);
        Assert.Equal(AgentId, agentEvent.AgentId);
        Assert.Equal(AgentRole.Research, agentEvent.Role);
        Assert.Equal(TicketAgentEventKind.Started, agentEvent.Kind);
        Assert.Null(agentEvent.Result);
        Assert.Null(agentEvent.InputTokens);
        Assert.Null(agentEvent.OutputTokens);
        Assert.Null(agentEvent.DurationMs);
    }

    [Fact]
    public void CreateCompleted_WithValidArguments_SetsResultAndUsage()
    {
        var agentEvent = TicketAgentEvent.CreateCompleted(TicketId, AgentId, AgentRole.Coding, "Implemented the change.", inputTokens: 120, outputTokens: 340, durationMs: 5500);

        Assert.Equal(TicketAgentEventKind.Completed, agentEvent.Kind);
        Assert.Equal("Implemented the change.", agentEvent.Result);
        Assert.Equal(120, agentEvent.InputTokens);
        Assert.Equal(340, agentEvent.OutputTokens);
        Assert.Equal(5500, agentEvent.DurationMs);
    }

    [Fact]
    public void CreateBlocked_WithValidArguments_SetsResultAndUsage()
    {
        var agentEvent = TicketAgentEvent.CreateBlocked(TicketId, AgentId, AgentRole.Testing, "What timeout should I use?", inputTokens: 80, outputTokens: 20, durationMs: 1200);

        Assert.Equal(TicketAgentEventKind.Blocked, agentEvent.Kind);
        Assert.Equal("What timeout should I use?", agentEvent.Result);
        Assert.Equal(80, agentEvent.InputTokens);
        Assert.Equal(20, agentEvent.OutputTokens);
        Assert.Equal(1200, agentEvent.DurationMs);
    }

    [Fact]
    public void CreateFailed_WithNullAgentAndRole_Succeeds()
    {
        var agentEvent = TicketAgentEvent.CreateFailed(TicketId, null, null, "Git push failed.", durationMs: null);

        Assert.Null(agentEvent.AgentId);
        Assert.Null(agentEvent.Role);
        Assert.Equal(TicketAgentEventKind.Failed, agentEvent.Kind);
        Assert.Equal("Git push failed.", agentEvent.Result);
        Assert.Null(agentEvent.InputTokens);
        Assert.Null(agentEvent.OutputTokens);
    }

    [Fact]
    public void CreateFailed_WithDurationMs_SetsDurationMsAndNoTokenUsage()
    {
        var agentEvent = TicketAgentEvent.CreateFailed(TicketId, AgentId, AgentRole.Coding, "Git push failed.", durationMs: 900);

        Assert.Equal(900, agentEvent.DurationMs);
        Assert.Null(agentEvent.InputTokens);
        Assert.Null(agentEvent.OutputTokens);
    }

    [Fact]
    public void CreateFailed_WithEmptyTicketId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => TicketAgentEvent.CreateFailed(Guid.Empty, AgentId, AgentRole.Coding, "Git push failed.", durationMs: null));
    }
}
