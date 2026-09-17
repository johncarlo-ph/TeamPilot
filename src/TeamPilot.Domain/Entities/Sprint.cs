using TeamPilot.Domain.Common;

namespace TeamPilot.Domain.Entities;

/// <summary>
/// A time-boxed unit of work inside a <see cref="Project"/>: its own branch (cut from and
/// merged back into the project's shared repository clone) and sprint details, owning its own
/// ticket board. Deliberately holds no navigation collection to <see cref="Ticket"/> - like
/// Project, it's a lightweight aggregate root; children reference it by <see cref="Entity.Id"/> only.
/// </summary>
public class Sprint : Entity
{
    public Guid ProjectId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>The branch every ticket's feature branch in this sprint is cut from, and that
    /// approvals merge back into.</summary>
    public string BaseBranch { get; private set; } = "main";

    /// <summary>When this sprint starts. Optional - a sprint can be used without framing it as
    /// time-boxed at all.</summary>
    public DateTime? SprintStartDate { get; private set; }

    /// <summary>When this sprint ends. Optional, and never before <see cref="SprintStartDate"/>
    /// when both are set - see <see cref="Create"/>/<see cref="UpdateDetails"/>.</summary>
    public DateTime? SprintEndDate { get; private set; }

    /// <summary>The sprint's goal/objective, free text. Optional.</summary>
    public string? SprintGoal { get; private set; }

    /// <summary>Set by <see cref="Remove"/>. Hides the sprint from every UI listing without
    /// touching its tickets or history - there is no "restore" in this pass, so removal is
    /// treated as one-way rather than a toggle.</summary>
    public bool IsRemoved { get; private set; }

    private Sprint()
    {
    }

    public static Sprint Create(
        Guid projectId,
        string name,
        string? baseBranch,
        DateTime? sprintStartDate = null,
        DateTime? sprintEndDate = null,
        string? sprintGoal = null)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project id is required.", nameof(projectId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Sprint name is required.", nameof(name));
        }

        if (sprintStartDate is not null && sprintEndDate is not null && sprintEndDate < sprintStartDate)
        {
            throw new ArgumentException("Sprint end date can't be before the sprint start date.", nameof(sprintEndDate));
        }

        return new Sprint
        {
            ProjectId = projectId,
            Name = name.Trim(),
            BaseBranch = string.IsNullOrWhiteSpace(baseBranch) ? "main" : baseBranch.Trim(),
            SprintStartDate = sprintStartDate,
            SprintEndDate = sprintEndDate,
            SprintGoal = sprintGoal?.Trim(),
        };
    }

    public void UpdateDetails(
        string name,
        string baseBranch,
        DateTime? sprintStartDate = null,
        DateTime? sprintEndDate = null,
        string? sprintGoal = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Sprint name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(baseBranch))
        {
            throw new ArgumentException("Base branch is required.", nameof(baseBranch));
        }

        if (sprintStartDate is not null && sprintEndDate is not null && sprintEndDate < sprintStartDate)
        {
            throw new ArgumentException("Sprint end date can't be before the sprint start date.", nameof(sprintEndDate));
        }

        Name = name.Trim();
        BaseBranch = baseBranch.Trim();
        SprintStartDate = sprintStartDate;
        SprintEndDate = sprintEndDate;
        SprintGoal = sprintGoal?.Trim();
        MarkUpdated();
    }

    /// <summary>Hides the sprint from the UI (<see cref="IsRemoved"/>). Whether it's actually
    /// safe to remove - e.g. no ticket still <c>InProgress</c>/<c>ForReview</c> - depends on
    /// sibling <c>Ticket</c> rows this entity can't see, so that check lives in
    /// <c>SprintService.RemoveAsync</c>, not here; this method only performs the flip once the
    /// use case has already decided it's allowed.</summary>
    public void Remove()
    {
        IsRemoved = true;
        MarkUpdated();
    }
}
