using Microsoft.AspNetCore.Mvc;
using TeamPilot.Application.Approval;
using TeamPilot.Application.Reviews.Dtos;
using TeamPilot.Application.Tickets.Dtos;
using TeamPilot.Domain.Entities;

namespace TeamPilot.API.Controllers;

[ApiController]
[Route("api/tickets/{ticketId:guid}/reviews")]
public class ReviewsController(IApprovalGateService approvalGateService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<TicketDto>> SubmitReview(Guid ticketId, [FromBody] SubmitReviewRequest request, CancellationToken cancellationToken)
    {
        var ticket = await approvalGateService.SubmitReviewAsync(ticketId, request, cancellationToken);
        return Ok(ticket);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReviewDto>>> ListByTicket(Guid ticketId, CancellationToken cancellationToken)
    {
        var reviews = await approvalGateService.ListByTicketAsync(ticketId, cancellationToken);
        return Ok(reviews.Select(ToDto));
    }

    private static ReviewDto ToDto(Review review) => new(
        review.Id, review.TicketId, review.ReviewerName, review.Decision, review.Comments, review.CreatedAtUtc);
}
