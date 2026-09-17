using FluentValidation;
using TeamPilot.Application.Sprints.Dtos;

namespace TeamPilot.Application.Sprints.Validators;

public sealed class CreateSprintRequestValidator : AbstractValidator<CreateSprintRequest>
{
    public CreateSprintRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BaseBranch).MaximumLength(200);
        RuleFor(x => x.SprintGoal).MaximumLength(1000);
        RuleFor(x => x)
            .Must(x => x.SprintStartDate is null || x.SprintEndDate is null || x.SprintEndDate >= x.SprintStartDate)
            .WithMessage("Sprint end date can't be before the sprint start date.")
            .WithName("SprintEndDate");
    }
}
