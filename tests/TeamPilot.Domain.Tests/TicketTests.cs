using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using TeamPilot.Domain.Exceptions;
using Xunit;

namespace TeamPilot.Domain.Tests;

public class TicketTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();

    [Fact]
    public void Create_WithValidTitle_SetsStatusToToDo()
    {
        var ticket = Ticket.Create(ProjectId, "Fix login bug", "Users can't log in");

        Assert.Equal(TicketStatus.ToDo, ticket.Status);
        Assert.Equal("Fix login bug", ticket.Title);
        Assert.Equal(ProjectId, ticket.ProjectId);
    }

    [Fact]
    public void Create_WithEmptyTitle_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Ticket.Create(ProjectId, "   ", "desc"));
    }

    [Fact]
    public void Create_WithEmptyProjectId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Ticket.Create(Guid.Empty, "Fix login bug", "desc"));
    }

    [Fact]
    public void AssignAgent_FirstAssignment_MovesTicketToInProgress()
    {
        var ticket = Ticket.Create(ProjectId, "Fix login bug", "desc");
        var agent = Agent.Create(ProjectId, "Coder", AgentRole.Coding);

        ticket.AssignAgent(agent);

        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.Single(ticket.Assignments);
    }

    [Fact]
    public void AssignAgent_SameAgentTwice_DoesNotDuplicateAssignment()
    {
        var ticket = Ticket.Create(ProjectId, "Fix login bug", "desc");
        var agent = Agent.Create(ProjectId, "Coder", AgentRole.Coding);

        ticket.AssignAgent(agent);
        ticket.AssignAgent(agent);

        Assert.Single(ticket.Assignments);
    }

    [Fact]
    public void MoveToReview_WhenStatusIsToDo_ThrowsInvalidTicketStateTransitionException()
    {
        var ticket = Ticket.Create(ProjectId, "Fix login bug", "desc");

        Assert.Throws<InvalidTicketStateTransitionException>(() => ticket.MoveToReview());
    }

    [Fact]
    public void MoveToReview_WhenStatusIsInProgress_SetsStatusToForReview()
    {
        var ticket = Ticket.Create(ProjectId, "Fix login bug", "desc");
        ticket.AssignAgent(Agent.Create(ProjectId, "Coder", AgentRole.Coding));

        ticket.MoveToReview();

        Assert.Equal(TicketStatus.ForReview, ticket.Status);
    }

    [Fact]
    public void Approve_WhenStatusIsForReview_SetsStatusToDone()
    {
        var ticket = Ticket.Create(ProjectId, "Fix login bug", "desc");
        ticket.AssignAgent(Agent.Create(ProjectId, "Coder", AgentRole.Coding));
        ticket.MoveToReview();

        ticket.Approve();

        Assert.Equal(TicketStatus.Done, ticket.Status);
    }

    [Fact]
    public void Approve_WhenStatusIsInProgress_ThrowsInvalidTicketStateTransitionException()
    {
        var ticket = Ticket.Create(ProjectId, "Fix login bug", "desc");
        ticket.AssignAgent(Agent.Create(ProjectId, "Coder", AgentRole.Coding));

        Assert.Throws<InvalidTicketStateTransitionException>(() => ticket.Approve());
    }

    [Fact]
    public void RequestChanges_WhenStatusIsForReview_SetsStatusToInProgress()
    {
        var ticket = Ticket.Create(ProjectId, "Fix login bug", "desc");
        ticket.AssignAgent(Agent.Create(ProjectId, "Coder", AgentRole.Coding));
        ticket.MoveToReview();

        ticket.RequestChanges();

        Assert.Equal(TicketStatus.InProgress, ticket.Status);
    }
}
