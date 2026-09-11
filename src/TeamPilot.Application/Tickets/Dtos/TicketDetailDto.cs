using TeamPilot.Application.Commits.Dtos;
using TeamPilot.Application.Conflicts.Dtos;
using TeamPilot.Application.Reviews.Dtos;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Tickets.Dtos;

public sealed record TicketDetailDto(
    Guid Id,
    Guid ProjectId,
    string Title,
    string Description,
    TicketStatus Status,
    string? BranchName,
    IReadOnlyCollection<TicketAgentAssignmentDto> Assignments,
    IReadOnlyCollection<CommitDto> Commits,
    IReadOnlyCollection<ReviewDto> Reviews,
    IReadOnlyCollection<ConflictDto> Conflicts,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);
