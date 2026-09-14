using FluentValidation;
using TeamPilot.Application.LiveAgentChat.Dtos;

namespace TeamPilot.Application.LiveAgentChat.Validators;

public sealed class RenameConversationRequestValidator : AbstractValidator<RenameConversationRequest>
{
    public RenameConversationRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
    }
}
