using FluentValidation;
using TeamPilot.Application.Agents.Dtos;

namespace TeamPilot.Application.Agents.Validators;

public sealed class CreateAgentRequestValidator : AbstractValidator<CreateAgentRequest>
{
    public CreateAgentRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Role).IsInEnum();
    }
}
