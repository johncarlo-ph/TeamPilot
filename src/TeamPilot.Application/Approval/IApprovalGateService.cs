using TeamPilot.Application.Reviews.Dtos;
using TeamPilot.Application.Tickets.Dtos;

namespace TeamPilot.Application.Approval;

public interface IApprovalGateService
{
    /// <summary>
    /// Records a human review against a ticket in "For Review" and applies its decision:
    /// Approve merges the ticket's branch and moves it to Done; RequestChanges moves it back to
    /// In Progress and immediately re-runs the project's workflow on it (the reviewer's comments
    /// are threaded into every stage's prompt on that run - see
    /// <c>Orchestration.OrchestrationService</c>); Reject cancels the ticket and deletes its
    /// linked branch, if any - a one-way door, unlike RequestChanges; ResolveConflict is recorded
    /// for the conflict-resolution workflow to pick up via <c>IConflictResolutionService</c>.
    /// </summary>
    Task<TicketDto> SubmitReviewAsync(Guid ticketId, SubmitReviewRequest request, CancellationToken cancellationToken = default);
}
