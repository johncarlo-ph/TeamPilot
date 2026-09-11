using FluentValidation;
using TeamPilot.Application.Orchestration.Dtos;

namespace TeamPilot.Application.Orchestration.Validators;

public sealed class AssignSubAgentsRequestValidator : AbstractValidator<AssignSubAgentsRequest>
{
    public AssignSubAgentsRequestValidator()
    {
        RuleFor(x => x.AgentIds).NotEmpty();
    }
}
