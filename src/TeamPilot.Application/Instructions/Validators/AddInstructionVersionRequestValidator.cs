using FluentValidation;
using TeamPilot.Application.Instructions.Dtos;

namespace TeamPilot.Application.Instructions.Validators;

public sealed class AddInstructionVersionRequestValidator : AbstractValidator<AddInstructionVersionRequest>
{
    public AddInstructionVersionRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Content).NotEmpty();
    }
}
