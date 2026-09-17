using FluentValidation;
using TeamPilot.Application.Projects.Dtos;

namespace TeamPilot.Application.Projects.Validators;

public sealed class UpdateProjectRequestValidator : AbstractValidator<UpdateProjectRequest>
{
    public UpdateProjectRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.BaseBranch).NotEmpty().MaximumLength(200);
        When(x => !string.IsNullOrEmpty(x.AccessToken), () =>
        {
            RuleFor(x => x.AccessToken).MaximumLength(500);
        });
        RuleFor(x => x.SprintGoal).MaximumLength(1000);
        RuleFor(x => x)
            .Must(x => x.SprintStartDate is null || x.SprintEndDate is null || x.SprintEndDate >= x.SprintStartDate)
            .WithMessage("Sprint end date can't be before the sprint start date.")
            .WithName("SprintEndDate");
    }
}
