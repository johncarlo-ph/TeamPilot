using TeamPilot.Domain.Common;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Domain.Entities;

/// <summary>
/// One turn in a project's <see cref="Conversation"/> with its Live Agent. An assistant message
/// that drafted a ticket carries <see cref="ProposedTicketTitle"/>/<see cref="ProposedTicketDescription"/>
/// so the "create ticket" approval card re-renders correctly after a reload - the ticket itself
/// isn't created until the user approves it, so nothing else on this message reflects that yet.
/// </summary>
public class ChatMessage : Entity
{
    public Guid ConversationId { get; private set; }

    public ChatMessageRole Role { get; private set; }

    public string Content { get; private set; } = string.Empty;

    public string? ProposedTicketTitle { get; private set; }

    public string? ProposedTicketDescription { get; private set; }

    private ChatMessage()
    {
    }

    internal static ChatMessage Create(
        Guid conversationId,
        ChatMessageRole role,
        string content,
        string? proposedTicketTitle,
        string? proposedTicketDescription)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Message content is required.", nameof(content));
        }

        return new ChatMessage
        {
            ConversationId = conversationId,
            Role = role,
            Content = content.Trim(),
            ProposedTicketTitle = string.IsNullOrWhiteSpace(proposedTicketTitle) ? null : proposedTicketTitle.Trim(),
            ProposedTicketDescription = string.IsNullOrWhiteSpace(proposedTicketDescription) ? null : proposedTicketDescription.Trim(),
        };
    }
}
