using FluentValidation;
using TeamPilot.Application.Tickets.Dtos;

namespace TeamPilot.Application.Tickets.Validators;

public sealed class CancelTicketRequestValidator : AbstractValidator<CancelTicketRequest>
{
    public CancelTicketRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(1000);
    }
}
