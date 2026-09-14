using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Tickets.Dtos;

/// <summary>
/// <paramref name="PipelineRunning"/> reports whether a pipeline run is actually executing for
/// this ticket right now (see <c>IPipelineRunTracker</c>) - <paramref name="Status"/> alone can't
/// tell a caller this: a ticket sits <c>InProgress</c> both while a run is actively executing and
/// while it's idle, waiting for a human to manually trigger one.
/// </summary>
public sealed record TicketDto(
    Guid Id,
    Guid ProjectId,
    string Title,
    string Description,
    TicketStatus Status,
    string? BranchName,
    string? CancellationReason,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    bool PipelineRunning = false);
