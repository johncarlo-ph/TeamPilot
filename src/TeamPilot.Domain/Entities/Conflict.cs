using TeamPilot.Domain.Common;
using TeamPilot.Domain.Enums;
using TeamPilot.Domain.Exceptions;

namespace TeamPilot.Domain.Entities;

/// <summary>
/// A detected merge conflict for a ticket's branch, with an optional AI-suggested resolution
/// that a human can accept or override with a manual resolution.
/// </summary>
public class Conflict : Entity
{
    public Guid TicketId { get; private set; }

    public Guid? CommitId { get; private set; }

    public string FilePath { get; private set; } = string.Empty;

    public string ConflictingDiffContent { get; private set; } = string.Empty;

    public string? AiSuggestedResolution { get; private set; }

    /// <summary>
    /// The resolution actually applied - either the human's manual note, or a copy of the
    /// AI suggestion once accepted. Distinct from <see cref="AiSuggestedResolution"/>, which
    /// always reflects what the AI proposed even if a human resolved it differently.
    /// </summary>
    public string? ResolutionNote { get; private set; }

    public ConflictStatus Status { get; private set; }

    public DateTime? ResolvedAtUtc { get; private set; }

    public string? ResolvedBy { get; private set; }

    private Conflict()
    {
    }

    public static Conflict Create(Guid ticketId, string filePath, string conflictingDiffContent, Guid? commitId = null)
    {
        if (ticketId == Guid.Empty)
        {
            throw new ArgumentException("Ticket id is required.", nameof(ticketId));
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path is required.", nameof(filePath));
        }

        return new Conflict
        {
            TicketId = ticketId,
            CommitId = commitId,
            FilePath = filePath,
            ConflictingDiffContent = conflictingDiffContent ?? string.Empty,
            Status = ConflictStatus.Detected,
        };
    }

    public void RecordAiSuggestion(string suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion))
        {
            throw new ArgumentException("Suggestion is required.", nameof(suggestion));
        }

        EnsureNotAlreadyResolved("record an AI suggestion");

        AiSuggestedResolution = suggestion;
        Status = ConflictStatus.AiResolutionSuggested;
        MarkUpdated();
    }

    public void ResolveManually(string note, string resolvedBy)
    {
        if (string.IsNullOrWhiteSpace(resolvedBy))
        {
            throw new ArgumentException("Resolved by is required.", nameof(resolvedBy));
        }

        EnsureNotAlreadyResolved("resolve manually");

        ResolutionNote = note;
        Status = ConflictStatus.ResolvedManually;
        ResolvedBy = resolvedBy.Trim();
        ResolvedAtUtc = DateTime.UtcNow;
        MarkUpdated();
    }

    public void AcceptAiSuggestion(string resolvedBy)
    {
        if (string.IsNullOrWhiteSpace(resolvedBy))
        {
            throw new ArgumentException("Resolved by is required.", nameof(resolvedBy));
        }

        if (AiSuggestedResolution is null)
        {
            throw new InvalidOperationException("No AI suggestion has been recorded for this conflict.");
        }

        EnsureNotAlreadyResolved("accept the AI suggestion");

        ResolutionNote = AiSuggestedResolution;
        Status = ConflictStatus.ResolvedWithAiSuggestion;
        ResolvedBy = resolvedBy.Trim();
        ResolvedAtUtc = DateTime.UtcNow;
        MarkUpdated();
    }

    private void EnsureNotAlreadyResolved(string attemptedAction)
    {
        if (Status is ConflictStatus.ResolvedManually or ConflictStatus.ResolvedWithAiSuggestion)
        {
            throw new InvalidConflictStateTransitionException(Status, attemptedAction);
        }
    }
}
