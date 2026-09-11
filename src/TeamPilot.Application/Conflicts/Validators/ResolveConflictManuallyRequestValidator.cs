using FluentValidation;
using TeamPilot.Application.Conflicts.Dtos;

namespace TeamPilot.Application.Conflicts.Validators;

public sealed class ResolveConflictManuallyRequestValidator : AbstractValidator<ResolveConflictManuallyRequest>
{
    public ResolveConflictManuallyRequestValidator()
    {
        RuleFor(x => x.Note).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.ResolvedBy).NotEmpty().MaximumLength(200);
    }
}
