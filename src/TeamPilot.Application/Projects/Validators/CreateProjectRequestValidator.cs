using FluentValidation;
using TeamPilot.Application.Projects.Dtos;

namespace TeamPilot.Application.Projects.Validators;

public sealed class CreateProjectRequestValidator : AbstractValidator<CreateProjectRequest>
{
    public CreateProjectRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.RepositoryPath).NotEmpty().MaximumLength(1000);
    }
}
