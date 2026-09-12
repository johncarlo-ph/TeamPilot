using FluentValidation;
using TeamPilot.Application.LiveAgentChat.Dtos;

namespace TeamPilot.Application.LiveAgentChat.Validators;

public sealed class SendChatMessageRequestValidator : AbstractValidator<SendChatMessageRequest>
{
    public SendChatMessageRequestValidator()
    {
        RuleFor(x => x.Content).NotEmpty().MaximumLength(4000);
    }
}
