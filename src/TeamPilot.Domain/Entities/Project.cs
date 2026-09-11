using TeamPilot.Domain.Common;

namespace TeamPilot.Domain.Entities;

/// <summary>
/// A project owns its own orchestrator/sub-agents, ticket board, Git repository, and CI/CD
/// pipeline runs. Deliberately holds no navigation collections to those - like Commit/Review/
/// Conflict on Ticket, they reference this entity by <see cref="Entity.Id"/> only, keeping
/// Project a lightweight aggregate root.
/// </summary>
public class Project : Entity
{
    public string Name { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    /// <summary>
    /// Path to this project's local, already-<c>git init</c>'d sandbox repository.
    /// </summary>
    public string RepositoryPath { get; private set; } = string.Empty;

    private Project()
    {
    }

    public static Project Create(string name, string? description, string repositoryPath)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Project name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(repositoryPath))
        {
            throw new ArgumentException("Repository path is required.", nameof(repositoryPath));
        }

        return new Project
        {
            Name = name.Trim(),
            Description = description?.Trim() ?? string.Empty,
            RepositoryPath = repositoryPath.Trim(),
        };
    }

    public void UpdateDetails(string name, string? description, string repositoryPath)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Project name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(repositoryPath))
        {
            throw new ArgumentException("Repository path is required.", nameof(repositoryPath));
        }

        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
        RepositoryPath = repositoryPath.Trim();
        MarkUpdated();
    }
}
