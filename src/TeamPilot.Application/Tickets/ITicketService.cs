using TeamPilot.Application.Tickets.Dtos;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Tickets;

public interface ITicketService
{
    Task<TicketDto> CreateAsync(Guid sprintId, CreateTicketRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a ticket in the project's backlog, not yet assigned to any sprint - see
    /// <see cref="AssignToSprintAsync"/>.
    /// </summary>
    Task<TicketDto> CreateBacklogAsync(Guid projectId, CreateTicketRequest request, CancellationToken cancellationToken = default);

    Task<TicketDetailDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TicketDto>> ListAsync(Guid sprintId, TicketStatus? status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lean, read-only listing of every ticket across a project's sprints - used by the Agents
    /// page's workflow-lock check (a project-wide concern, not scoped to one sprint's board) and
    /// the Live Agent chat's ticket tool.
    /// </summary>
    Task<IReadOnlyList<TicketDto>> ListByProjectAsync(Guid projectId, TicketStatus? status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lean, read-only listing of a project's backlog - tickets not yet assigned to any sprint.
    /// </summary>
    Task<IReadOnlyList<TicketDto>> ListBacklogAsync(Guid projectId, TicketStatus? status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a backlog ticket into a sprint - only allowed once, from no sprint currently
    /// assigned (see <see cref="TeamPilot.Domain.Entities.Ticket.AssignToSprint"/>). Throws
    /// <see cref="Common.Exceptions.NotFoundException"/> if <paramref name="sprintId"/> doesn't
    /// belong to the ticket's own project.
    /// </summary>
    Task<TicketDto> AssignToSprintAsync(Guid ticketId, AssignTicketToSprintRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a ticket back out of its sprint into the project's backlog - only allowed while
    /// still <c>ToDo</c> and with no linked branch (see
    /// <see cref="TeamPilot.Domain.Entities.Ticket.MoveToBacklog"/>).
    /// </summary>
    Task<TicketDto> MoveToBacklogAsync(Guid ticketId, CancellationToken cancellationToken = default);

    Task<TicketDto> MoveToReviewAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Abandons the ticket - allowed from any pre-merge status (<c>ToDo</c>, <c>InProgress</c>,
    /// <c>ForReview</c>); once <c>Done</c>, the work is already merged and there's nothing left
    /// to cancel.
    /// </summary>
    Task<TicketDto> CancelAsync(Guid id, CancelTicketRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the ticket's linked branch from the remote and the local sandbox, then clears
    /// <see cref="TeamPilot.Domain.Entities.Ticket.BranchName"/> - only allowed for a
    /// <c>Cancelled</c> ticket (see <see cref="TeamPilot.Domain.Entities.Ticket.UnlinkBranch"/>).
    /// </summary>
    Task<TicketDto> DeleteBranchAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the branch exists in the ticket's project's sandbox Git repository and links
    /// it to the ticket.
    /// </summary>
    Task<TicketDto> LinkBranchAsync(Guid ticketId, string branchName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the ticket's project remote and reports whether <paramref name="branchName"/>
    /// already exists (locally or on the remote), without creating or linking anything.
    /// </summary>
    Task<bool> BranchExistsAsync(Guid ticketId, string branchName, CancellationToken cancellationToken = default);
}
