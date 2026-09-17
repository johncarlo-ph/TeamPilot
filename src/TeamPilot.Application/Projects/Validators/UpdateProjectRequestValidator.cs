using FluentValidation;
using TeamPilot.Application.Projects.Dtos;

namespace TeamPilot.Application.Projects.Validators;

public sealed class UpdateProjectRequestValidator : AbstractValidator<UpdateProjectRequest>
{
    public UpdateProjectRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        When(x => !string.IsNullOrEmpty(x.AccessToken), () =>
        {
            RuleFor(x => x.AccessToken).MaximumLength(500);
        });
    }
}
