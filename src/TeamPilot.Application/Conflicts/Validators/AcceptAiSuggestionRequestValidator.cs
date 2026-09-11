using FluentValidation;
using TeamPilot.Application.Conflicts.Dtos;

namespace TeamPilot.Application.Conflicts.Validators;

public sealed class AcceptAiSuggestionRequestValidator : AbstractValidator<AcceptAiSuggestionRequest>
{
    public AcceptAiSuggestionRequestValidator()
    {
        RuleFor(x => x.ResolvedBy).NotEmpty().MaximumLength(200);
    }
}
