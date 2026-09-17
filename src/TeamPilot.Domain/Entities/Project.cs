using TeamPilot.Domain.Common;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Domain.Entities;

/// <summary>
/// A project owns its own orchestrator/sub-agents, ticket board, remote Git repository, and
/// CI/CD pipeline runs. Deliberately holds no navigation collections to those - like Commit/
/// Review/Conflict on Ticket, they reference this entity by <see cref="Entity.Id"/> only,
/// keeping Project a lightweight aggregate root.
/// </summary>
public class Project : Entity
{
    public string Name { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    /// <summary>HTTPS URL of the remote repository this project is connected to. Immutable
    /// after creation - re-pointing it would orphan the existing sandbox clone.</summary>
    public string RemoteUrl { get; private set; } = string.Empty;

    /// <summary>The remote's access token (a PAT), encrypted at rest via
    /// IGitCredentialProtector. Never exposed outside the Infrastructure Git integration.</summary>
    public string EncryptedAccessToken { get; private set; } = string.Empty;

    /// <summary>The branch every ticket's feature branch is cut from, and that approvals merge
    /// back into (replaces a hardcoded "main").</summary>
    public string BaseBranch { get; private set; } = "main";

    /// <summary>
    /// Path to this project's server-managed local sandbox clone of <see cref="RemoteUrl"/>.
    /// Not user input - empty until <see cref="MarkCloned"/> records where the initial clone
    /// (run detached from project creation - see <c>ProjectService.CreateAsync</c>) landed.
    /// </summary>
    public string RepositoryPath { get; private set; } = string.Empty;

    /// <summary>Where this project is in cloning its remote repository. A project row exists
    /// (and is visible in the UI) from the moment it's created, before the clone even starts -
    /// see <see cref="Create"/>.</summary>
    public ProjectStatus Status { get; private set; }

    /// <summary>Why the clone failed, set by <see cref="MarkCloneFailed"/>. Always
    /// <see langword="null"/> unless <see cref="Status"/> is <see cref="ProjectStatus.Failed"/>.</summary>
    public string? CloneFailureReason { get; private set; }

    /// <summary>Set by <see cref="Remove"/>. Hides the project from every UI listing without
    /// touching its remote repository, sandbox clone, tickets, or history - there is no "restore"
    /// in this pass, so removal is treated as one-way rather than a toggle.</summary>
    public bool IsRemoved { get; private set; }

    /// <summary>When this project's sprint starts. Optional - a project can be used without
    /// framing it as a time-boxed sprint at all.</summary>
    public DateTime? SprintStartDate { get; private set; }

    /// <summary>When this project's sprint ends. Optional, and never before
    /// <see cref="SprintStartDate"/> when both are set - see <see cref="Create"/>/<see cref="UpdateDetails"/>.</summary>
    public DateTime? SprintEndDate { get; private set; }

    /// <summary>The sprint's goal/objective, free text. Optional.</summary>
    public string? SprintGoal { get; private set; }

    private Project()
    {
    }

    public static Project Create(
        string name,
        string? description,
        string remoteUrl,
        string encryptedAccessToken,
        string? baseBranch,
        DateTime? sprintStartDate = null,
        DateTime? sprintEndDate = null,
        string? sprintGoal = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Project name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            throw new ArgumentException("Remote URL is required.", nameof(remoteUrl));
        }

        if (string.IsNullOrWhiteSpace(encryptedAccessToken))
        {
            throw new ArgumentException("Access token is required.", nameof(encryptedAccessToken));
        }

        if (sprintStartDate is not null && sprintEndDate is not null && sprintEndDate < sprintStartDate)
        {
            throw new ArgumentException("Sprint end date can't be before the sprint start date.", nameof(sprintEndDate));
        }

        return new Project
        {
            Name = name.Trim(),
            Description = description?.Trim() ?? string.Empty,
            RemoteUrl = remoteUrl.Trim(),
            EncryptedAccessToken = encryptedAccessToken,
            BaseBranch = string.IsNullOrWhiteSpace(baseBranch) ? "main" : baseBranch.Trim(),
            Status = ProjectStatus.Cloning,
            SprintStartDate = sprintStartDate,
            SprintEndDate = sprintEndDate,
            SprintGoal = sprintGoal?.Trim(),
        };
    }

    /// <summary>Records where the initial clone of <see cref="RemoteUrl"/> landed and marks the
    /// project ready to use. Called exactly once, by the detached clone task right after a
    /// successful clone (see <c>ProjectService.CreateAsync</c>).</summary>
    public void MarkCloned(string repositoryPath)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
        {
            throw new ArgumentException("Repository path is required.", nameof(repositoryPath));
        }

        RepositoryPath = repositoryPath.Trim();
        Status = ProjectStatus.Ready;
        CloneFailureReason = null;
        MarkUpdated();
    }

    /// <summary>Records that the initial clone failed. The project row stays visible (in
    /// <see cref="ProjectStatus.Failed"/>) rather than disappearing, so the failure - and the
    /// remote URL/name a retry would need - aren't lost.</summary>
    public void MarkCloneFailed(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Failure reason is required.", nameof(reason));
        }

        Status = ProjectStatus.Failed;
        CloneFailureReason = reason.Trim();
        MarkUpdated();
    }

    public void UpdateDetails(
        string name,
        string? description,
        string baseBranch,
        DateTime? sprintStartDate = null,
        DateTime? sprintEndDate = null,
        string? sprintGoal = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Project name is required.", nameof(name));
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
        Description = description?.Trim() ?? string.Empty;
        BaseBranch = baseBranch.Trim();
        SprintStartDate = sprintStartDate;
        SprintEndDate = sprintEndDate;
        SprintGoal = sprintGoal?.Trim();
        MarkUpdated();
    }

    /// <summary>Replaces the stored (encrypted) access token, e.g. after the PAT is rotated on
    /// the remote host. Kept separate from UpdateDetails so token rotation is an explicit,
    /// auditable operation.</summary>
    public void RotateAccessToken(string encryptedAccessToken)
    {
        if (string.IsNullOrWhiteSpace(encryptedAccessToken))
        {
            throw new ArgumentException("Access token is required.", nameof(encryptedAccessToken));
        }

        EncryptedAccessToken = encryptedAccessToken;
        MarkUpdated();
    }

    /// <summary>Hides the project from the UI (<see cref="IsRemoved"/>). Whether it's actually
    /// safe to remove - e.g. no ticket still <c>InProgress</c>/<c>ForReview</c> - depends on
    /// sibling <c>Ticket</c> rows this entity can't see, so that check lives in
    /// <c>ProjectService.RemoveAsync</c>, not here; this method only performs the flip once the
    /// use case has already decided it's allowed.</summary>
    public void Remove()
    {
        IsRemoved = true;
        MarkUpdated();
    }
}
