using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Conflicts.Dtos;

public sealed record ConflictDto(
    Guid Id,
    Guid TicketId,
    Guid? CommitId,
    string FilePath,
    string ConflictingDiffContent,
    string? AiSuggestedResolution,
    string? ResolvedContent,
    string? ResolutionNote,
    ConflictStatus Status,
    DateTime? ResolvedAtUtc,
    string? ResolvedBy,
    string? BaseTipSha,
    DateTime CreatedAtUtc);
