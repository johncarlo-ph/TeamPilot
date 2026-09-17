namespace TeamPilot.Application.Sprints.Dtos;

/// <summary>
/// Per-status ticket counts for a sprint's board, used by the sprint list's summary badges.
/// Mirrors the UI's BOARD_COLUMNS set - Cancelled is intentionally excluded, same as the board
/// itself.
/// </summary>
public sealed record TicketStatusCountsDto(
    int ToDo,
    int InProgress,
    int Blocked,
    int ForReview,
    int Done);
