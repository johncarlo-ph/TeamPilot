using TeamPilot.Application.LiveAgentChat.Dtos;

namespace TeamPilot.Application.LiveAgentChat;

public interface ILiveAgentChatService
{
    Task<IReadOnlyList<ChatMessageDto>> GetHistoryAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a user message to the project's Live Agent and returns its reply. Internally runs a
    /// bounded tool-use loop (read-only repo file access, ticket listing, ticket drafting) before
    /// the assistant's final text response is persisted and returned.
    /// </summary>
    Task<ChatMessageDto> SendMessageAsync(Guid projectId, SendChatMessageRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves a drafted ticket from an assistant message, creating the real <see cref="TeamPilot.Domain.Entities.Ticket"/>
    /// and stamping the message with its id so every viewer (including after a reload) sees the
    /// draft as already approved. Idempotent: approving an already-approved message just returns
    /// its current state instead of creating a duplicate ticket.
    /// </summary>
    Task<ChatMessageDto> ApproveTicketAsync(Guid projectId, Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dismisses a drafted ticket from an assistant message without creating anything, so the
    /// "Create ticket"/"Reject" pair permanently disappears from that message's approval card for
    /// every viewer, including after a reload. Idempotent: rejecting an already-rejected message
    /// just returns its current state. Conflicts (409) if the message's ticket was already
    /// created - the two decisions are mutually exclusive once made.
    /// </summary>
    Task<ChatMessageDto> RejectTicketAsync(Guid projectId, Guid messageId, CancellationToken cancellationToken = default);
}
