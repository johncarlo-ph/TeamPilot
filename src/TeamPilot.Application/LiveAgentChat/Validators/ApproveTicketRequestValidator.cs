using FluentValidation;
using TeamPilot.Application.LiveAgentChat.Dtos;

namespace TeamPilot.Application.LiveAgentChat.Validators;

public sealed class ApproveTicketRequestValidator : AbstractValidator<ApproveTicketRequest>
{
    public ApproveTicketRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(4000);
    }
}
