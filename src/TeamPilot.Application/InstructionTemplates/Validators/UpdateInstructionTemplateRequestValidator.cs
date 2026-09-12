using FluentValidation;
using TeamPilot.Application.InstructionTemplates.Dtos;

namespace TeamPilot.Application.InstructionTemplates.Validators;

public sealed class UpdateInstructionTemplateRequestValidator : AbstractValidator<UpdateInstructionTemplateRequest>
{
    public UpdateInstructionTemplateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Content).NotEmpty();
    }
}
