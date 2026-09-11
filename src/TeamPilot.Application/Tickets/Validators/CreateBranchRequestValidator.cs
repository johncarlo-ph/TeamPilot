using FluentValidation;
using TeamPilot.Application.Tickets.Dtos;

namespace TeamPilot.Application.Tickets.Validators;

public sealed class CreateBranchRequestValidator : AbstractValidator<CreateBranchRequest>
{
    public CreateBranchRequestValidator()
    {
        RuleFor(x => x.TicketId).NotEmpty();
        RuleFor(x => x.BranchName)
            .NotEmpty()
            .MaximumLength(200)
            .Matches("^[A-Za-z0-9._/-]+$")
            .WithMessage("Branch name may only contain letters, digits, '.', '_', '/' and '-'.");
    }
}
