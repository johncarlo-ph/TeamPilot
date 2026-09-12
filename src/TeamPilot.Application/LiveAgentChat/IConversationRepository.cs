using TeamPilot.Domain.Entities;

namespace TeamPilot.Application.LiveAgentChat;

public interface IConversationRepository
{
    /// <summary>
    /// Loads the project's single conversation with its messages (oldest first), or
    /// <see langword="null"/> if the project hasn't chatted with its Live Agent yet.
    /// </summary>
    Task<Conversation?> GetByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task AddAsync(Conversation conversation, CancellationToken cancellationToken = default);
}
