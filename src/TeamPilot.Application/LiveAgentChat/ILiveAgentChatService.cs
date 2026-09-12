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
}
