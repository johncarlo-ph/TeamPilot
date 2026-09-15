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
    public void CreateStarted_WithValidArguments_SetsFieldsAndNullResult()
    {
        var agentEvent = TicketAgentEvent.CreateStarted(TicketId, AgentId, AgentRole.Research);

        Assert.Equal(TicketId, agentEvent.TicketId);
        Assert.Equal(AgentId, agentEvent.AgentId);
        Assert.Equal(AgentRole.Research, agentEvent.Role);
        Assert.Equal(TicketAgentEventKind.Started, agentEvent.Kind);
        Assert.Null(agentEvent.Result);
    }

    [Fact]
    public void CreateCompleted_WithValidArguments_SetsResult()
    {
        var agentEvent = TicketAgentEvent.CreateCompleted(TicketId, AgentId, AgentRole.Coding, "Implemented the change.");

        Assert.Equal(TicketAgentEventKind.Completed, agentEvent.Kind);
        Assert.Equal("Implemented the change.", agentEvent.Result);
    }

    [Fact]
    public void CreateBlocked_WithValidArguments_SetsResult()
    {
        var agentEvent = TicketAgentEvent.CreateBlocked(TicketId, AgentId, AgentRole.Testing, "What timeout should I use?");

        Assert.Equal(TicketAgentEventKind.Blocked, agentEvent.Kind);
        Assert.Equal("What timeout should I use?", agentEvent.Result);
    }

    [Fact]
    public void CreateFailed_WithNullAgentAndRole_Succeeds()
    {
        var agentEvent = TicketAgentEvent.CreateFailed(TicketId, null, null, "Git push failed.");

        Assert.Null(agentEvent.AgentId);
        Assert.Null(agentEvent.Role);
        Assert.Equal(TicketAgentEventKind.Failed, agentEvent.Kind);
        Assert.Equal("Git push failed.", agentEvent.Result);
    }

    [Fact]
    public void CreateFailed_WithEmptyTicketId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => TicketAgentEvent.CreateFailed(Guid.Empty, AgentId, AgentRole.Coding, "Git push failed."));
    }
}
