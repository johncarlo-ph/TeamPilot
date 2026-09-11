using FluentValidation;
using TeamPilot.Application.Pipelines.Dtos;

namespace TeamPilot.Application.Pipelines.Validators;

public sealed class CompletePipelineRunRequestValidator : AbstractValidator<CompletePipelineRunRequest>
{
    public CompletePipelineRunRequestValidator()
    {
        RuleFor(x => x.LogOutput).MaximumLength(8000);
    }
}
