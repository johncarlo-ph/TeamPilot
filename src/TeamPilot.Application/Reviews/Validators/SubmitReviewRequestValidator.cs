using FluentValidation;
using TeamPilot.Application.Reviews.Dtos;

namespace TeamPilot.Application.Reviews.Validators;

public sealed class SubmitReviewRequestValidator : AbstractValidator<SubmitReviewRequest>
{
    public SubmitReviewRequestValidator()
    {
        RuleFor(x => x.ReviewerName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Decision).IsInEnum();
        RuleFor(x => x.Comments).MaximumLength(4000);
    }
}
