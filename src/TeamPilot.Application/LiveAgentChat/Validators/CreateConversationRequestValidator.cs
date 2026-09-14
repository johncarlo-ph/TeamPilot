using FluentValidation;
using TeamPilot.Application.LiveAgentChat.Dtos;

namespace TeamPilot.Application.LiveAgentChat.Validators;

public sealed class CreateConversationRequestValidator : AbstractValidator<CreateConversationRequest>
{
    public CreateConversationRequestValidator()
    {
        RuleFor(x => x.Title).MaximumLength(200);
    }
}
