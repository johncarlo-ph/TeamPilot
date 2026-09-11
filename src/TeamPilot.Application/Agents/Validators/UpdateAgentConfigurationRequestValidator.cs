using FluentValidation;
using TeamPilot.Application.Agents.Dtos;

namespace TeamPilot.Application.Agents.Validators;

public sealed class UpdateAgentConfigurationRequestValidator : AbstractValidator<UpdateAgentConfigurationRequest>
{
    public UpdateAgentConfigurationRequestValidator()
    {
        RuleFor(x => x.ConfigurationJson).NotEmpty();
    }
}
