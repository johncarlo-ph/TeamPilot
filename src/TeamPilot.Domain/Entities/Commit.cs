using TeamPilot.Domain.Common;

namespace TeamPilot.Domain.Entities;

/// <summary>
/// A fact record of a Git commit produced (typically by a coding agent) for a ticket's branch.
/// </summary>
public class Commit : Entity
{
    public Guid TicketId { get; private set; }

    public string BranchName { get; private set; } = string.Empty;

    public string CommitHash { get; private set; } = string.Empty;

    public string Message { get; private set; } = string.Empty;

    public string DiffContent { get; private set; } = string.Empty;

    /// <summary>
    /// The agent that authored this commit, or <c>null</c> if it was made by a human.
    /// </summary>
    public Guid? AuthorAgentId { get; private set; }

    private Commit()
    {
    }

    public static Commit Create(
        Guid ticketId,
        string branchName,
        string commitHash,
        string message,
        string diffContent,
        Guid? authorAgentId = null)
    {
        if (ticketId == Guid.Empty)
        {
            throw new ArgumentException("Ticket id is required.", nameof(ticketId));
        }

        if (string.IsNullOrWhiteSpace(branchName))
        {
            throw new ArgumentException("Branch name is required.", nameof(branchName));
        }

        if (string.IsNullOrWhiteSpace(commitHash))
        {
            throw new ArgumentException("Commit hash is required.", nameof(commitHash));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Commit message is required.", nameof(message));
        }

        return new Commit
        {
            TicketId = ticketId,
            BranchName = branchName,
            CommitHash = commitHash,
            Message = message,
            DiffContent = diffContent ?? string.Empty,
            AuthorAgentId = authorAgentId,
        };
    }
}
