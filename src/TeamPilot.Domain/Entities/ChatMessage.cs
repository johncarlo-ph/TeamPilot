using TeamPilot.Domain.Common;
using TeamPilot.Domain.Enums;
using TeamPilot.Domain.Exceptions;

namespace TeamPilot.Domain.Entities;

/// <summary>
/// One turn in a project's <see cref="Conversation"/> with its Live Agent. An assistant message
/// that drafted a ticket carries <see cref="ProposedTicketTitle"/>/<see cref="ProposedTicketDescription"/>
/// so the "create ticket" approval card re-renders correctly after a reload - the ticket itself
/// isn't created until the user approves it, at which point <see cref="CreatedTicketId"/> is set
/// so every viewer (including after a reload) sees the draft as already approved.
/// </summary>
public class ChatMessage : Entity
{
    public Guid ConversationId { get; private set; }

    public ChatMessageRole Role { get; private set; }

    public string Content { get; private set; } = string.Empty;

    public string? ProposedTicketTitle { get; private set; }

    public string? ProposedTicketDescription { get; private set; }

    /// <summary>
    /// Id of the real <see cref="Ticket"/> created from this message's proposal, once the user
    /// approves it. Null until then; always null for a message with no proposal.
    /// </summary>
    public Guid? CreatedTicketId { get; private set; }

    /// <summary>
    /// Whether the user explicitly dismissed this message's proposal instead of approving it.
    /// Recorded (rather than left implicit) so the "Create ticket"/"Reject" pair permanently
    /// disappears from the approval card for every viewer, including after a reload, once either
    /// decision has been made - the same reasoning as <see cref="CreatedTicketId"/>.
    /// </summary>
    public bool TicketRejected { get; private set; }

    /// <summary>
    /// Display name of the user who sent this message, captured at send time so the chat
    /// history keeps showing who said what even if the user is later renamed or removed.
    /// Null for <see cref="ChatMessageRole.Assistant"/> messages.
    /// </summary>
    public string? SenderName { get; private set; }

    private ChatMessage()
    {
    }

    /// <summary>
    /// Records the real <see cref="Ticket"/> created from this message's proposal. Callers must
    /// check <see cref="CreatedTicketId"/> first and treat an already-approved message as a
    /// no-op rather than calling this twice - it throws rather than silently overwriting, since
    /// a second, different ticket id here would almost certainly indicate a caller bug.
    /// </summary>
    public void MarkTicketCreated(Guid ticketId)
    {
        if (string.IsNullOrWhiteSpace(ProposedTicketTitle))
        {
            throw new ChatMessageTicketApprovalException("This message has no proposed ticket to approve.");
        }

        if (CreatedTicketId is not null)
        {
            throw new ChatMessageTicketApprovalException("This message's proposed ticket has already been created.");
        }

        if (TicketRejected)
        {
            throw new ChatMessageTicketApprovalException("This message's proposed ticket was already rejected.");
        }

        CreatedTicketId = ticketId;
        MarkUpdated();
    }

    /// <summary>
    /// Records that the user dismissed this message's proposal rather than approving it.
    /// Callers must check <see cref="TicketRejected"/> first and treat an already-rejected
    /// message as a no-op rather than calling this twice, same pattern as
    /// <see cref="MarkTicketCreated"/>.
    /// </summary>
    public void RejectTicket()
    {
        if (string.IsNullOrWhiteSpace(ProposedTicketTitle))
        {
            throw new ChatMessageTicketApprovalException("This message has no proposed ticket to reject.");
        }

        if (CreatedTicketId is not null)
        {
            throw new ChatMessageTicketApprovalException("This message's proposed ticket has already been created.");
        }

        if (TicketRejected)
        {
            throw new ChatMessageTicketApprovalException("This message's proposed ticket has already been rejected.");
        }

        TicketRejected = true;
        MarkUpdated();
    }

    internal static ChatMessage Create(
        Guid conversationId,
        ChatMessageRole role,
        string content,
        string? proposedTicketTitle,
        string? proposedTicketDescription,
        string? senderName = null)
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
            SenderName = string.IsNullOrWhiteSpace(senderName) ? null : senderName.Trim(),
        };
    }
}
