using TeamPilot.Domain.Common;

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
    /// Not user input - assigned once, by <see cref="AssignSandboxPath"/>, right after the
    /// initial clone succeeds.
    /// </summary>
    public string RepositoryPath { get; private set; } = string.Empty;

    private Project()
    {
    }

    public static Project Create(string name, string? description, string remoteUrl, string encryptedAccessToken, string? baseBranch)
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

        return new Project
        {
            Name = name.Trim(),
            Description = description?.Trim() ?? string.Empty,
            RemoteUrl = remoteUrl.Trim(),
            EncryptedAccessToken = encryptedAccessToken,
            BaseBranch = string.IsNullOrWhiteSpace(baseBranch) ? "main" : baseBranch.Trim(),
        };
    }

    /// <summary>Records where the initial clone of <see cref="RemoteUrl"/> landed. Called
    /// exactly once, by ProjectService right after a successful clone.</summary>
    public void AssignSandboxPath(string repositoryPath)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
        {
            throw new ArgumentException("Repository path is required.", nameof(repositoryPath));
        }

        RepositoryPath = repositoryPath.Trim();
    }

    public void UpdateDetails(string name, string? description, string baseBranch)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Project name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(baseBranch))
        {
            throw new ArgumentException("Base branch is required.", nameof(baseBranch));
        }

        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
        BaseBranch = baseBranch.Trim();
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
}
