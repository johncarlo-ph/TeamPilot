using TeamPilot.Application.LiveAgentChat.Dtos;

namespace TeamPilot.Application.LiveAgentChat;

public interface ILiveAgentChatService
{
    /// <summary>
    /// Lists a project's chat sessions with its Live Agent, most recently updated first - every
    /// project member sees the full list, regardless of who started each one.
    /// </summary>
    Task<IReadOnlyList<ConversationDto>> ListConversationsAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a new chat session for the current user. Unlike a message send, this never lazily
    /// reuses an existing conversation - each call creates a distinct session.
    /// </summary>
    Task<ConversationDto> CreateConversationAsync(Guid projectId, CreateConversationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a chat session. Any project member can rename any session - a conversation isn't
    /// private to the user who started it.
    /// </summary>
    Task<ConversationDto> RenameConversationAsync(Guid projectId, Guid conversationId, RenameConversationRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatMessageDto>> GetHistoryAsync(Guid projectId, Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a user message to the project's Live Agent within one chat session and returns its
    /// reply. Internally runs a bounded tool-use loop (read-only repo file access, ticket listing,
    /// ticket drafting) before the assistant's final text response is persisted and returned.
    /// </summary>
    Task<ChatMessageDto> SendMessageAsync(Guid projectId, Guid conversationId, SendChatMessageRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves a drafted ticket from an assistant message, creating the real <see cref="TeamPilot.Domain.Entities.Ticket"/>
    /// and stamping the message with its id so every viewer (including after a reload) sees the
    /// draft as already approved. Idempotent: approving an already-approved message just returns
    /// its current state instead of creating a duplicate ticket.
    /// </summary>
    Task<ChatMessageDto> ApproveTicketAsync(Guid projectId, Guid conversationId, Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dismisses a drafted ticket from an assistant message without creating anything, so the
    /// "Create ticket"/"Reject" pair permanently disappears from that message's approval card for
    /// every viewer, including after a reload. Idempotent: rejecting an already-rejected message
    /// just returns its current state. Conflicts (409) if the message's ticket was already
    /// created - the two decisions are mutually exclusive once made.
    /// </summary>
    Task<ChatMessageDto> RejectTicketAsync(Guid projectId, Guid conversationId, Guid messageId, CancellationToken cancellationToken = default);
}
