using FluentValidation;
using TeamPilot.Application.TicketQuestions.Dtos;

namespace TeamPilot.Application.TicketQuestions.Validators;

public sealed class AnswerTicketQuestionRequestValidator : AbstractValidator<AnswerTicketQuestionRequest>
{
    public AnswerTicketQuestionRequestValidator()
    {
        RuleFor(x => x.Answer).NotEmpty().MaximumLength(4000);
    }
}
