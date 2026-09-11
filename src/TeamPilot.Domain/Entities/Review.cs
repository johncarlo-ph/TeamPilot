using TeamPilot.Domain.Common;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Domain.Entities;

/// <summary>
/// A human reviewer's decision recorded against a ticket during the approval gate.
/// References its ticket by id only; cross-aggregate coordination (moving the ticket,
/// attempting a merge, etc.) is the responsibility of an Application-layer service.
/// </summary>
public class Review : Entity
{
    public Guid TicketId { get; private set; }

    public string ReviewerName { get; private set; } = string.Empty;

    public ReviewDecision Decision { get; private set; }

    public string? Comments { get; private set; }

    private Review()
    {
    }

    public static Review Create(Guid ticketId, string reviewerName, ReviewDecision decision, string? comments)
    {
        if (ticketId == Guid.Empty)
        {
            throw new ArgumentException("Ticket id is required.", nameof(ticketId));
        }

        if (string.IsNullOrWhiteSpace(reviewerName))
        {
            throw new ArgumentException("Reviewer name is required.", nameof(reviewerName));
        }

        return new Review
        {
            TicketId = ticketId,
            ReviewerName = reviewerName.Trim(),
            Decision = decision,
            Comments = comments,
        };
    }
}
