using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Reviews.Dtos;

public sealed record SubmitReviewRequest(ReviewDecision Decision, string? Comments);
