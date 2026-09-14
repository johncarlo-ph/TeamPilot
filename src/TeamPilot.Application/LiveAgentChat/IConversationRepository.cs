using TeamPilot.Domain.Entities;

namespace TeamPilot.Application.LiveAgentChat;

public interface IConversationRepository
{
    /// <summary>
    /// Lists a project's conversations, most recently updated first, without their messages
    /// (only needed for a full history load - see <see cref="GetByIdAsync"/>).
    /// </summary>
    Task<IReadOnlyList<Conversation>> ListByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads one conversation with its messages (oldest first), or <see langword="null"/> if no
    /// conversation with that id exists.
    /// </summary>
    Task<Conversation?> GetByIdAsync(Guid conversationId, CancellationToken cancellationToken = default);

    Task AddAsync(Conversation conversation, CancellationToken cancellationToken = default);
}
