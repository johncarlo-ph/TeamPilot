using FluentValidation;
using TeamPilot.Application.Tickets.Dtos;

namespace TeamPilot.Application.Tickets.Validators;

public sealed class CreateTicketRequestValidator : AbstractValidator<CreateTicketRequest>
{
    public CreateTicketRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.AcceptanceCriteria).NotEmpty().MaximumLength(4000);
    }
}
