using TeamPilot.Application.Commits.Dtos;
using TeamPilot.Application.Conflicts.Dtos;
using TeamPilot.Application.Reviews.Dtos;
using TeamPilot.Application.TicketPipelineNotes.Dtos;
using TeamPilot.Application.TicketProjectInstructions.Dtos;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Tickets.Dtos;

/// <summary>
/// <paramref name="PipelineRunning"/> reports whether a pipeline run is actually executing for
/// this ticket right now (see <c>IPipelineRunTracker</c>) - <paramref name="Status"/> alone can't
/// tell a caller this: a ticket sits <c>InProgress</c> both while a run is actively executing and
/// while it's idle, waiting for a human to manually trigger one. <paramref name="Instructions"/>
/// are non-blocking "this referenced project needs a change" notes a Research/Design stage raised
/// (see <c>TicketProjectInstruction</c>) - read-only, for a human to act on manually.
/// <paramref name="PipelineNotes"/> are non-blocking human-facing notes any pipeline stage chose
/// to leave behind (see <c>TicketPipelineNote</c> and the <c>NOTES:</c> marker in
/// <c>OrchestrationService.BuildStagePrompt</c>) - most useful once the ticket reaches
/// <see cref="TicketStatus.Done"/>, where there's no more agent output to dig through.
/// </summary>
public sealed record TicketDetailDto(
    Guid Id,
    Guid ProjectId,
    Guid? SprintId,
    string Title,
    string Description,
    string AcceptanceCriteria,
    TicketStatus Status,
    string? BranchName,
    string? CancellationReason,
    IReadOnlyCollection<TicketAgentAssignmentDto> Assignments,
    IReadOnlyCollection<CommitDto> Commits,
    IReadOnlyCollection<ReviewDto> Reviews,
    IReadOnlyCollection<ConflictDto> Conflicts,
    IReadOnlyCollection<TicketProjectInstructionDto> Instructions,
    IReadOnlyCollection<TicketPipelineNoteDto> PipelineNotes,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    bool PipelineRunning = false);
