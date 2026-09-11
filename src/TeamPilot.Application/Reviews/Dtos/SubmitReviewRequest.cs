using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Reviews.Dtos;

public sealed record SubmitReviewRequest(string ReviewerName, ReviewDecision Decision, string? Comments);
