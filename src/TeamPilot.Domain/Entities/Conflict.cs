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

    /// <summary>
    /// The complete, resolved file content the LLM proposed - not a prose description - shown to
    /// a human to review before accepting. Only copied into <see cref="ResolvedContent"/> (the
    /// content actually used) once accepted via <see cref="AcceptAiSuggestion"/>.
    /// </summary>
    public string? AiSuggestedResolution { get; private set; }

    /// <summary>
    /// The complete file content to write at <see cref="FilePath"/> once this conflict's
    /// resolution is applied - either what a human typed in directly (<see cref="ResolveManually"/>)
    /// or a copy of <see cref="AiSuggestedResolution"/> once accepted. This is what
    /// <c>TeamPilot.Application.Approval.ApprovalGateService.ApproveAsync</c> reads to complete
    /// the real merge - see docs/application.md.
    /// </summary>
    public string? ResolvedContent { get; private set; }

    /// <summary>
    /// An optional short human-readable note about a manual resolution (e.g. "kept both changes")
    /// - informational only, never what gets written to the file. Not set for an AI-accepted
    /// resolution, since the status itself already says what happened.
    /// </summary>
    public string? ResolutionNote { get; private set; }

    public ConflictStatus Status { get; private set; }

    public DateTime? ResolvedAtUtc { get; private set; }

    public string? ResolvedBy { get; private set; }

    /// <summary>
    /// The base branch's commit SHA at the moment this conflict was detected - stamped once, in
    /// <see cref="Create"/>, and carried forward unchanged through <see cref="ResolveManually"/>/
    /// <see cref="AcceptAiSuggestion"/>. <c>IGitService.MergeWithResolutionsAsync</c> re-checks it
    /// against the base branch's live tip right before applying a resolution - if the base branch
    /// has moved further in the meantime, the resolution is stale and gets reset via
    /// <see cref="MarkStale"/> instead of being applied blindly. <see langword="null"/> only for a
    /// conflict created before this field existed.
    /// </summary>
    public string? BaseTipSha { get; private set; }

    private Conflict()
    {
    }

    public static Conflict Create(Guid ticketId, string filePath, string conflictingDiffContent, string? baseTipSha = null, Guid? commitId = null)
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
            BaseTipSha = baseTipSha,
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

    public void ResolveManually(string resolvedContent, string? note, string resolvedBy)
    {
        if (string.IsNullOrWhiteSpace(resolvedContent))
        {
            throw new ArgumentException("Resolved content is required.", nameof(resolvedContent));
        }

        if (string.IsNullOrWhiteSpace(resolvedBy))
        {
            throw new ArgumentException("Resolved by is required.", nameof(resolvedBy));
        }

        EnsureNotAlreadyResolved("resolve manually");

        ResolvedContent = resolvedContent;
        ResolutionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
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

        ResolvedContent = AiSuggestedResolution;
        Status = ConflictStatus.ResolvedWithAiSuggestion;
        ResolvedBy = resolvedBy.Trim();
        ResolvedAtUtc = DateTime.UtcNow;
        MarkUpdated();
    }

    /// <summary>
    /// Un-resolves this conflict after the live merge attempt found its resolution was prepared
    /// against a base-branch tip that's since moved further (see <see cref="BaseTipSha"/>) -
    /// resets it back to <see cref="ConflictStatus.Detected"/> so a reviewer sees it as needing
    /// resolution again, without discarding the AI suggestion (still a reasonable starting point)
    /// or the original <see cref="BaseTipSha"/> (the "Detect Conflicts" action, not this, is what
    /// refreshes <see cref="ConflictingDiffContent"/> and <see cref="BaseTipSha"/> together).
    /// </summary>
    public void MarkStale()
    {
        ResolvedContent = null;
        ResolutionNote = null;
        ResolvedBy = null;
        ResolvedAtUtc = null;
        Status = ConflictStatus.Detected;
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
