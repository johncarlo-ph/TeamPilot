using FluentValidation;
using TeamPilot.Application.Pipelines.Dtos;

namespace TeamPilot.Application.Pipelines.Validators;

public sealed class TriggerPipelineRunRequestValidator : AbstractValidator<TriggerPipelineRunRequest>
{
    public TriggerPipelineRunRequestValidator()
    {
        RuleFor(x => x.TriggerReason).NotEmpty().MaximumLength(500);
    }
}
