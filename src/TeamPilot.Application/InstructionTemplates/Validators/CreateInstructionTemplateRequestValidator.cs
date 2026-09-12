using FluentValidation;
using TeamPilot.Application.InstructionTemplates.Dtos;

namespace TeamPilot.Application.InstructionTemplates.Validators;

public sealed class CreateInstructionTemplateRequestValidator : AbstractValidator<CreateInstructionTemplateRequest>
{
    public CreateInstructionTemplateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Role).IsInEnum();
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Content).NotEmpty();
    }
}
