using TeamPilot.Application.Reviews.Dtos;
using TeamPilot.Application.Tickets.Dtos;

namespace TeamPilot.Application.Approval;

public interface IApprovalGateService
{
    /// <summary>
    /// Records a human review against a ticket in "For Review" and applies its decision:
    /// Approve merges the ticket's branch and moves it to Done; RequestChanges moves it back
    /// to In Progress; ResolveConflict is recorded for the conflict-resolution workflow to
    /// pick up via <c>IConflictResolutionService</c>.
    /// </summary>
    Task<TicketDto> SubmitReviewAsync(Guid ticketId, SubmitReviewRequest request, CancellationToken cancellationToken = default);
}
