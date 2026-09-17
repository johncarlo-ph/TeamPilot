using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using TeamPilot.Domain.Exceptions;
using Xunit;

namespace TeamPilot.Domain.Tests;

public class TicketTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();

    [Fact]
    public void Create_WithValidTitle_SetsStatusToToDo()
    {
        var ticket = Ticket.Create(ProjectId, SprintId, "Fix login bug", "Users can't log in", "Acceptance criteria");

        Assert.Equal(TicketStatus.ToDo, ticket.Status);
        Assert.Equal("Fix login bug", ticket.Title);
        Assert.Equal(ProjectId, ticket.ProjectId);
        Assert.Equal(SprintId, ticket.SprintId);
    }

    [Fact]
    public void Create_WithEmptyTitle_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Ticket.Create(ProjectId, SprintId, "   ", "desc", "Acceptance criteria"));
    }

    [Fact]
    public void Create_WithEmptyProjectId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Ticket.Create(Guid.Empty, SprintId, "Fix login bug", "desc", "Acceptance criteria"));
    }

    [Fact]
    public void Create_WithEmptySprintId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Ticket.Create(ProjectId, Guid.Empty, "Fix login bug", "desc", "Acceptance criteria"));
    }

    [Fact]
    public void Create_WithEmptyAcceptanceCriteria_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Ticket.Create(ProjectId, SprintId, "Fix login bug", "desc", "   "));
    }

    [Fact]
    public void Create_WithNoSprintId_LeavesItNull()
    {
        var ticket = Ticket.Create(ProjectId, null, "Fix login bug", "desc", "Acceptance criteria");

        Assert.Null(ticket.SprintId);
        Assert.Equal(ProjectId, ticket.ProjectId);
    }

    [Fact]
    public void AssignToSprint_WhenNotYetAssigned_SetsSprintId()
    {
        var ticket = Ticket.Create(ProjectId, null, "Fix login bug", "desc", "Acceptance criteria");

        ticket.AssignToSprint(SprintId);

        Assert.Equal(SprintId, ticket.SprintId);
        Assert.NotNull(ticket.UpdatedAtUtc);
    }

    [Fact]
    public void AssignToSprint_WithEmptySprintId_ThrowsArgumentException()
    {
        var ticket = Ticket.Create(ProjectId, null, "Fix login bug", "desc", "Acceptance criteria");

        Assert.Throws<ArgumentException>(() => ticket.AssignToSprint(Guid.Empty));
    }

    [Fact]
    public void AssignToSprint_WhenAlreadyAssigned_ThrowsTicketAlreadyAssignedToSprintException()
    {
        var ticket = Ticket.Create(ProjectId, SprintId, "Fix login bug", "desc", "Acceptance criteria");

        Assert.Throws<TicketAlreadyAssignedToSprintException>(() => ticket.AssignToSprint(Guid.NewGuid()));
    }

    [Fact]
    public void MoveToBacklog_WhenToDoWithNoBranch_ClearsSprintId()
    {
        var ticket = Ticket.Create(ProjectId, SprintId, "Fix login bug", "desc", "Acceptance criteria");

        ticket.MoveToBacklog();

        Assert.Null(ticket.SprintId);
    }

    [Fact]
    public void MoveToBacklog_WhenNotToDo_ThrowsInvalidTicketStateTransitionException()
    {
        var ticket = Ticket.Create(ProjectId, SprintId, "Fix login bug", "desc", "Acceptance criteria");
        ticket.AssignAgent(Agent.Create(ProjectId, "Coder", AgentRole.Coding));

        Assert.Throws<InvalidTicketStateTransitionException>(() => ticket.MoveToBacklog());
    }

    [Fact]
    public void MoveToBacklog_WhenTicketHasLinkedBranch_ThrowsTicketHasLinkedBranchException()
    {
        var ticket = Ticket.Create(ProjectId, SprintId, "Fix login bug", "desc", "Acceptance criteria");
        ticket.LinkBranch("feature/fix-login-bug");

        Assert.Throws<TicketHasLinkedBranchException>(() => ticket.MoveToBacklog());
    }

    [Fact]
    public void AssignAgent_FirstAssignment_MovesTicketToInProgress()
    {
        var ticket = Ticket.Create(ProjectId, SprintId, "Fix login bug", "desc", "Acceptance criteria");
        var agent = Agent.Create(ProjectId, "Coder", AgentRole.Coding);

        ticket.AssignAgent(agent);

        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.Single(ticket.Assignments);
    }

    [Fact]
    public void AssignAgent_SameAgentTwice_DoesNotDuplicateAssignment()
    {
        var ticket = Ticket.Create(ProjectId, SprintId, "Fix login bug", "desc", "Acceptance criteria");
        var agent = Agent.Create(ProjectId, "Coder", AgentRole.Coding);

        ticket.AssignAgent(agent);
        ticket.AssignAgent(agent);

        Assert.Single(ticket.Assignments);
    }

    [Fact]
    public void MoveToReview_WhenStatusIsToDo_ThrowsInvalidTicketStateTransitionException()
    {
        var ticket = Ticket.Create(ProjectId, SprintId, "Fix login bug", "desc", "Acceptance criteria");

        Assert.Throws<InvalidTicketStateTransitionException>(() => ticket.MoveToReview());
    }

    [Fact]
    public void MoveToReview_WhenStatusIsInProgress_SetsStatusToForReview()
    {
        var ticket = Ticket.Create(ProjectId, SprintId, "Fix login bug", "desc", "Acceptance criteria");
        ticket.AssignAgent(Agent.Create(ProjectId, "Coder", AgentRole.Coding));

        ticket.MoveToReview();

        Assert.Equal(TicketStatus.ForReview, ticket.Status);
    }

    [Fact]
    public void Approve_WhenStatusIsForReview_SetsStatusToDone()
    {
        var ticket = Ticket.Create(ProjectId, SprintId, "Fix login bug", "desc", "Acceptance criteria");
        ticket.AssignAgent(Agent.Create(ProjectId, "Coder", AgentRole.Coding));
        ticket.MoveToReview();

        ticket.Approve();

        Assert.Equal(TicketStatus.Done, ticket.Status);
    }

    [Fact]
    public void Approve_WhenStatusIsInProgress_ThrowsInvalidTicketStateTransitionException()
    {
        var ticket = Ticket.Create(ProjectId, SprintId, "Fix login bug", "desc", "Acceptance criteria");
        ticket.AssignAgent(Agent.Create(ProjectId, "Coder", AgentRole.Coding));

        Assert.Throws<InvalidTicketStateTransitionException>(() => ticket.Approve());
    }

    [Fact]
    public void RequestChanges_WhenStatusIsForReview_SetsStatusToInProgress()
    {
        var ticket = Ticket.Create(ProjectId, SprintId, "Fix login bug", "desc", "Acceptance criteria");
        ticket.AssignAgent(Agent.Create(ProjectId, "Coder", AgentRole.Coding));
        ticket.MoveToReview();

        ticket.RequestChanges();

        Assert.Equal(TicketStatus.InProgress, ticket.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Cancel_WhenStatusIsToDo_SetsStatusToCancelledAndTreatsBlankReasonAsNull(string? reason)
    {
        var ticket = Ticket.Create(ProjectId, SprintId, "Fix login bug", "desc", "Acceptance criteria");

        ticket.Cancel(reason);

        Assert.Equal(TicketStatus.Cancelled, ticket.Status);
        Assert.Null(ticket.CancellationReason);
    }

    [Fact]
    public void Cancel_WhenStatusIsInProgress_SetsStatusToCancelledAndTrimsReason()
    {
        var ticket = Ticket.Create(ProjectId, SprintId, "Fix login bug", "desc", "Acceptance criteria");
        ticket.AssignAgent(Agent.Create(ProjectId, "Coder", AgentRole.Coding));

        ticket.Cancel("  Requirement changed  ");

        Assert.Equal(TicketStatus.Cancelled, ticket.Status);
        Assert.Equal("Requirement changed", ticket.CancellationReason);
    }

    [Fact]
    public void Cancel_WhenStatusIsForReview_SetsStatusToCancelled()
    {
        var ticket = Ticket.Create(ProjectId, SprintId, "Fix login bug", "desc", "Acceptance criteria");
        ticket.AssignAgent(Agent.Create(ProjectId, "Coder", AgentRole.Coding));
        ticket.MoveToReview();

        ticket.Cancel("No longer needed");

        Assert.Equal(TicketStatus.Cancelled, ticket.Status);
    }

    [Fact]
    public void Cancel_WhenStatusIsDone_ThrowsInvalidTicketStateTransitionException()
    {
        var ticket = Ticket.Create(ProjectId, SprintId, "Fix login bug", "desc", "Acceptance criteria");
        ticket.AssignAgent(Agent.Create(ProjectId, "Coder", AgentRole.Coding));
        ticket.MoveToReview();
        ticket.Approve();

        Assert.Throws<InvalidTicketStateTransitionException>(() => ticket.Cancel("Too late"));
    }

    [Fact]
    public void Cancel_WhenAlreadyCancelled_ThrowsInvalidTicketStateTransitionException()
    {
        var ticket = Ticket.Create(ProjectId, SprintId, "Fix login bug", "desc", "Acceptance criteria");
        ticket.Cancel("First reason");

        Assert.Throws<InvalidTicketStateTransitionException>(() => ticket.Cancel("Second reason"));
    }

    [Fact]
    public void LinkBranch_WhenCancelled_ThrowsInvalidTicketStateTransitionException()
    {
        var ticket = Ticket.Create(ProjectId, SprintId, "Fix login bug", "desc", "Acceptance criteria");
        ticket.Cancel("No longer needed");

        Assert.Throws<InvalidTicketStateTransitionException>(() => ticket.LinkBranch("feature/fix-login-bug"));
    }

    [Fact]
    public void UnlinkBranch_WhenCancelled_ClearsBranchName()
    {
        var ticket = Ticket.Create(ProjectId, SprintId, "Fix login bug", "desc", "Acceptance criteria");
        ticket.LinkBranch("feature/fix-login-bug");
        ticket.Cancel("No longer needed");

        ticket.UnlinkBranch();

        Assert.Null(ticket.BranchName);
    }

    [Theory]
    [InlineData(nameof(TicketStatus.ToDo))]
    [InlineData(nameof(TicketStatus.InProgress))]
    [InlineData(nameof(TicketStatus.ForReview))]
    [InlineData(nameof(TicketStatus.Done))]
    public void UnlinkBranch_WhenNotCancelled_ThrowsInvalidTicketStateTransitionException(string statusName)
    {
        var ticket = Ticket.Create(ProjectId, SprintId, "Fix login bug", "desc", "Acceptance criteria");
        ticket.AssignAgent(Agent.Create(ProjectId, "Coder", AgentRole.Coding));
        ticket.LinkBranch("feature/fix-login-bug");

        switch (Enum.Parse<TicketStatus>(statusName))
        {
            case TicketStatus.ForReview:
                ticket.MoveToReview();
                break;
            case TicketStatus.Done:
                ticket.MoveToReview();
                ticket.Approve();
                break;
        }

        Assert.Throws<InvalidTicketStateTransitionException>(() => ticket.UnlinkBranch());
    }

    [Fact]
    public void Block_WhenStatusIsInProgress_SetsStatusToBlocked()
    {
        var ticket = Ticket.Create(ProjectId, SprintId, "Fix login bug", "desc", "Acceptance criteria");
        ticket.AssignAgent(Agent.Create(ProjectId, "Coder", AgentRole.Coding));

        ticket.Block();

        Assert.Equal(TicketStatus.Blocked, ticket.Status);
    }

    [Theory]
    [InlineData(nameof(TicketStatus.ToDo))]
    [InlineData(nameof(TicketStatus.ForReview))]
    [InlineData(nameof(TicketStatus.Done))]
    [InlineData(nameof(TicketStatus.Cancelled))]
    public void Block_WhenNotInProgress_ThrowsInvalidTicketStateTransitionException(string statusName)
    {
        var ticket = Ticket.Create(ProjectId, SprintId, "Fix login bug", "desc", "Acceptance criteria");

        switch (Enum.Parse<TicketStatus>(statusName))
        {
            case TicketStatus.ForReview:
                ticket.AssignAgent(Agent.Create(ProjectId, "Coder", AgentRole.Coding));
                ticket.MoveToReview();
                break;
            case TicketStatus.Done:
                ticket.AssignAgent(Agent.Create(ProjectId, "Coder", AgentRole.Coding));
                ticket.MoveToReview();
                ticket.Approve();
                break;
            case TicketStatus.Cancelled:
                ticket.Cancel(null);
                break;
        }

        Assert.Throws<InvalidTicketStateTransitionException>(() => ticket.Block());
    }

    [Fact]
    public void Unblock_WhenStatusIsBlocked_SetsStatusToInProgress()
    {
        var ticket = Ticket.Create(ProjectId, SprintId, "Fix login bug", "desc", "Acceptance criteria");
        ticket.AssignAgent(Agent.Create(ProjectId, "Coder", AgentRole.Coding));
        ticket.Block();

        ticket.Unblock();

        Assert.Equal(TicketStatus.InProgress, ticket.Status);
    }

    [Fact]
    public void Unblock_WhenNotBlocked_ThrowsInvalidTicketStateTransitionException()
    {
        var ticket = Ticket.Create(ProjectId, SprintId, "Fix login bug", "desc", "Acceptance criteria");
        ticket.AssignAgent(Agent.Create(ProjectId, "Coder", AgentRole.Coding));

        Assert.Throws<InvalidTicketStateTransitionException>(() => ticket.Unblock());
    }

    [Fact]
    public void Cancel_WhenStatusIsBlocked_SetsStatusToCancelled()
    {
        var ticket = Ticket.Create(ProjectId, SprintId, "Fix login bug", "desc", "Acceptance criteria");
        ticket.AssignAgent(Agent.Create(ProjectId, "Coder", AgentRole.Coding));
        ticket.Block();

        ticket.Cancel("Giving up");

        Assert.Equal(TicketStatus.Cancelled, ticket.Status);
    }
}
