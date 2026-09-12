using FluentValidation;
using TeamPilot.Application.Workflow.Dtos;

namespace TeamPilot.Application.Workflow.Validators;

public sealed class CreateCustomAgentRequestValidator : AbstractValidator<CreateCustomAgentRequest>
{
    public CreateCustomAgentRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}
