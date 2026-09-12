using TeamPilot.Application.Commits.Dtos;
using TeamPilot.Application.Conflicts.Dtos;
using TeamPilot.Application.Reviews.Dtos;
using TeamPilot.Application.Tickets.Dtos;
using TeamPilot.Domain.Entities;

namespace TeamPilot.Application.Tickets;

/// <summary>
/// Shared entity-to-DTO mapping for <see cref="Ticket"/>, reused by <see cref="TicketService"/>
/// and the other services that mutate tickets (orchestration, approval gate).
/// </summary>
internal static class TicketMappings
{
    public static TicketDto ToDto(Ticket ticket) => new(
        ticket.Id,
        ticket.ProjectId,
        ticket.Title,
        ticket.Description,
        ticket.Status,
        ticket.BranchName,
        ticket.CancellationReason,
        ticket.CreatedAtUtc,
        ticket.UpdatedAtUtc);

    public static TicketDetailDto ToDetailDto(Ticket ticket) => new(
        ticket.Id,
        ticket.ProjectId,
        ticket.Title,
        ticket.Description,
        ticket.Status,
        ticket.BranchName,
        ticket.CancellationReason,
        ticket.Assignments
            .Select(a => new TicketAgentAssignmentDto(a.AgentId, a.RoleAtAssignment, a.AssignedAtUtc))
            .ToList(),
        ticket.Commits
            .Select(c => new CommitDto(c.Id, c.TicketId, c.BranchName, c.CommitHash, c.Message, c.DiffContent, c.AuthorAgentId, c.CreatedAtUtc))
            .ToList(),
        ticket.Reviews
            .Select(r => new ReviewDto(r.Id, r.TicketId, r.ReviewerName, r.Decision, r.Comments, r.CreatedAtUtc))
            .ToList(),
        ticket.Conflicts
            .Select(c => new ConflictDto(c.Id, c.TicketId, c.CommitId, c.FilePath, c.ConflictingDiffContent, c.AiSuggestedResolution, c.ResolvedContent, c.ResolutionNote, c.Status, c.ResolvedAtUtc, c.ResolvedBy, c.CreatedAtUtc))
            .ToList(),
        ticket.CreatedAtUtc,
        ticket.UpdatedAtUtc);
}
