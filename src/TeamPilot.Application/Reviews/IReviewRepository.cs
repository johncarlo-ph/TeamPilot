using TeamPilot.Domain.Entities;

namespace TeamPilot.Application.Reviews;

/// <summary>
/// Read-only access to reviews. New reviews are created through the <see cref="Ticket"/>
/// aggregate (<see cref="Ticket.RecordReview"/>) via <c>IApprovalGateService</c>.
/// </summary>
public interface IReviewRepository
{
    Task<IReadOnlyList<Review>> ListByTicketAsync(Guid ticketId, CancellationToken cancellationToken = default);
}
