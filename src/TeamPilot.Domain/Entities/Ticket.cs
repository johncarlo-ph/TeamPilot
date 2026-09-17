using TeamPilot.Domain.Common;
using TeamPilot.Domain.Enums;
using TeamPilot.Domain.Exceptions;

namespace TeamPilot.Domain.Entities;

/// <summary>
/// The aggregate root representing a unit of work moving through the Kanban board
/// (To Do -&gt; In Progress -&gt; For Review -&gt; Done), with its assigned agents, commits,
/// reviews, and detected conflicts.
/// </summary>
public class Ticket : Entity
{
    private readonly List<TicketAgentAssignment> _assignments = [];
    private readonly List<Commit> _commits = [];
    private readonly List<Review> _reviews = [];
    private readonly List<Conflict> _conflicts = [];

    /// <summary>Denormalized from the owning <see cref="Sprint"/>'s <see cref="Sprint.ProjectId"/>
    /// at creation - lets access control and git-branch uniqueness stay keyed on project id
    /// without a join through Sprint on every check.</summary>
    public Guid ProjectId { get; private set; }

    /// <summary>Null while the ticket sits in the project's backlog, not yet assigned to a
    /// sprint - see <see cref="AssignToSprint"/>.</summary>
    public Guid? SprintId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    /// <summary>The condition(s) this story must satisfy to be considered done. Required at
    /// creation, unlike <see cref="Description"/> - it's what the Research/Design pipeline stages
    /// use to scope their work (see <c>OrchestrationService.BuildStagePrompt</c>) and what the
    /// human approval gate ultimately checks against.</summary>
    public string AcceptanceCriteria { get; private set; } = string.Empty;

    public TicketStatus Status { get; private set; }

    public string? BranchName { get; private set; }

    public string? CancellationReason { get; private set; }

    public IReadOnlyCollection<TicketAgentAssignment> Assignments => _assignments.AsReadOnly();

    public IReadOnlyCollection<Commit> Commits => _commits.AsReadOnly();

    public IReadOnlyCollection<Review> Reviews => _reviews.AsReadOnly();

    public IReadOnlyCollection<Conflict> Conflicts => _conflicts.AsReadOnly();

    private Ticket()
    {
    }

    public static Ticket Create(Guid projectId, Guid? sprintId, string title, string? description, string acceptanceCriteria)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project id is required.", nameof(projectId));
        }

        if (sprintId == Guid.Empty)
        {
            throw new ArgumentException("Sprint id, when given, can't be empty.", nameof(sprintId));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title is required.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(acceptanceCriteria))
        {
            throw new ArgumentException("Acceptance criteria is required.", nameof(acceptanceCriteria));
        }

        return new Ticket
        {
            ProjectId = projectId,
            SprintId = sprintId,
            Title = title.Trim(),
            Description = description?.Trim() ?? string.Empty,
            AcceptanceCriteria = acceptanceCriteria.Trim(),
            Status = TicketStatus.ToDo,
        };
    }

    /// <summary>
    /// Moves the ticket out of the project's backlog into a sprint - only allowed once, from
    /// <see cref="SprintId"/> being <see langword="null"/>. Re-assigning an already-scheduled
    /// ticket to a different sprint isn't supported.
    /// </summary>
    public void AssignToSprint(Guid sprintId)
    {
        if (sprintId == Guid.Empty)
        {
            throw new ArgumentException("Sprint id is required.", nameof(sprintId));
        }

        if (SprintId is not null)
        {
            throw new TicketAlreadyAssignedToSprintException(Title);
        }

        SprintId = sprintId;
        MarkUpdated();
    }

    /// <summary>
    /// Moves the ticket back out of its sprint into the project's backlog - only allowed while
    /// still <see cref="TicketStatus.ToDo"/> (nothing has started yet) and with no linked branch
    /// (one would have been cut from this sprint's base branch, so it'd be left pointing at the
    /// wrong sprint once unassigned).
    /// </summary>
    public void MoveToBacklog()
    {
        if (Status != TicketStatus.ToDo)
        {
            throw new InvalidTicketStateTransitionException(Status, "move to the backlog");
        }

        if (!string.IsNullOrWhiteSpace(BranchName))
        {
            throw new TicketHasLinkedBranchException(Title, "moved to the backlog");
        }

        SprintId = null;
        MarkUpdated();
    }

    /// <summary>
    /// Assigns an agent to this ticket. The first assignment moves the ticket out of To Do.
    /// Assigning the same agent twice is a no-op.
    /// </summary>
    public void AssignAgent(Agent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        if (Status is not (TicketStatus.ToDo or TicketStatus.InProgress))
        {
            throw new InvalidTicketStateTransitionException(Status, "assign an agent");
        }

        if (_assignments.Any(a => a.AgentId == agent.Id))
        {
            return;
        }

        _assignments.Add(TicketAgentAssignment.Create(Id, agent.Id, agent.Role));

        if (Status == TicketStatus.ToDo)
        {
            Status = TicketStatus.InProgress;
        }

        MarkUpdated();
    }

    public void LinkBranch(string branchName)
    {
        if (string.IsNullOrWhiteSpace(branchName))
        {
            throw new ArgumentException("Branch name is required.", nameof(branchName));
        }

        // Guards against a gap UnlinkBranch would otherwise open: deleting a Cancelled ticket's
        // branch clears BranchName, which would make this terminal, abandoned ticket look like
        // it's eligible to link a fresh branch and resume work.
        if (Status == TicketStatus.Cancelled)
        {
            throw new InvalidTicketStateTransitionException(Status, "link a branch");
        }

        BranchName = branchName;
        MarkUpdated();
    }

    public void AddCommit(Commit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        _commits.Add(commit);
        MarkUpdated();
    }

    public void MoveToReview()
    {
        if (Status != TicketStatus.InProgress)
        {
            throw new InvalidTicketStateTransitionException(Status, "move to review");
        }

        Status = TicketStatus.ForReview;
        MarkUpdated();
    }

    public void Approve()
    {
        if (Status != TicketStatus.ForReview)
        {
            throw new InvalidTicketStateTransitionException(Status, "approve");
        }

        Status = TicketStatus.Done;
        MarkUpdated();
    }

    public void RequestChanges()
    {
        if (Status != TicketStatus.ForReview)
        {
            throw new InvalidTicketStateTransitionException(Status, "request changes");
        }

        Status = TicketStatus.InProgress;
        MarkUpdated();
    }

    /// <summary>
    /// Pauses the ticket mid-pipeline because an agent stage asked a clarifying question or a
    /// known operational failure (Git/LLM) occurred - see <see cref="TicketQuestion"/> for the
    /// record of what actually happened. Only reachable while the pipeline is actually running.
    /// </summary>
    public void Block()
    {
        if (Status != TicketStatus.InProgress)
        {
            throw new InvalidTicketStateTransitionException(Status, "block the ticket");
        }

        Status = TicketStatus.Blocked;
        MarkUpdated();
    }

    /// <summary>
    /// Resumes a blocked ticket - called once the blocking question has been answered, or a
    /// blocking failure is being retried, immediately before re-invoking the pipeline. There is
    /// no direct <see cref="Blocked"/> -&gt; <see cref="TicketStatus.ForReview"/> shortcut: a
    /// blocked ticket must go through the pipeline again to reach review.
    /// </summary>
    public void Unblock()
    {
        if (Status != TicketStatus.Blocked)
        {
            throw new InvalidTicketStateTransitionException(Status, "unblock the ticket");
        }

        Status = TicketStatus.InProgress;
        MarkUpdated();
    }

    public void RecordReview(Review review)
    {
        ArgumentNullException.ThrowIfNull(review);

        if (review.TicketId != Id)
        {
            throw new ArgumentException("Review does not belong to this ticket.", nameof(review));
        }

        _reviews.Add(review);
        MarkUpdated();
    }

    public void RaiseConflict(Conflict conflict)
    {
        ArgumentNullException.ThrowIfNull(conflict);

        if (conflict.TicketId != Id)
        {
            throw new ArgumentException("Conflict does not belong to this ticket.", nameof(conflict));
        }

        _conflicts.Add(conflict);
        MarkUpdated();
    }

    /// <summary>
    /// Abandons the ticket - terminal, like <see cref="Approve"/>, but reachable from any
    /// pre-merge state (unlike <see cref="Approve"/>, which only applies from <see cref="TicketStatus.ForReview"/>).
    /// Once <see cref="TicketStatus.Done"/>, the work is already merged, so there's nothing left
    /// to abandon.
    /// </summary>
    public void Cancel(string? reason)
    {
        if (Status is not (TicketStatus.ToDo or TicketStatus.InProgress or TicketStatus.ForReview or TicketStatus.Blocked))
        {
            throw new InvalidTicketStateTransitionException(Status, "cancel");
        }

        Status = TicketStatus.Cancelled;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        MarkUpdated();
    }

    /// <summary>
    /// Clears the linked branch after it's been deleted from Git - only allowed once the ticket
    /// is <see cref="TicketStatus.Cancelled"/>, since that's the only state where the branch's
    /// commits are known to be abandoned rather than still-needed work. This is also what frees
    /// the branch name up for a different ticket to claim (see the unique index on
    /// <c>(ProjectId, BranchName)</c>) - reusing a name whose branch still exists would silently
    /// mix its old commits into the new ticket.
    /// </summary>
    public void UnlinkBranch()
    {
        if (Status != TicketStatus.Cancelled)
        {
            throw new InvalidTicketStateTransitionException(Status, "delete the linked branch");
        }

        BranchName = null;
        MarkUpdated();
    }
}
