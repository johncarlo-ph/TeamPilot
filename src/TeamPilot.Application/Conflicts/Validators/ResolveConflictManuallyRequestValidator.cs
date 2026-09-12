using FluentValidation;
using TeamPilot.Application.Conflicts.Dtos;

namespace TeamPilot.Application.Conflicts.Validators;

public sealed class ResolveConflictManuallyRequestValidator : AbstractValidator<ResolveConflictManuallyRequest>
{
    public ResolveConflictManuallyRequestValidator()
    {
        RuleFor(x => x.ResolvedContent).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(4000);
        RuleFor(x => x.ResolvedBy).NotEmpty().MaximumLength(200);
    }
}
