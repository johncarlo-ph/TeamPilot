using FluentValidation;
using TeamPilot.Application.Tickets.Dtos;

namespace TeamPilot.Application.Tickets.Validators;

public sealed class AssignTicketToSprintRequestValidator : AbstractValidator<AssignTicketToSprintRequest>
{
    public AssignTicketToSprintRequestValidator()
    {
        RuleFor(x => x.SprintId).NotEmpty();
    }
}
