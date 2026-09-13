using TeamPilot.Domain.Common;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Domain.Entities;

/// <summary>
/// A project's single, ongoing chat thread with its Live Agent (see <see cref="AgentRole.LiveAgent"/>).
/// Exactly one per project - created lazily on the project's first chat message.
/// </summary>
public class Conversation : Entity
{
    private readonly List<ChatMessage> _messages = [];

    public Guid ProjectId { get; private set; }

    public Guid AgentId { get; private set; }

    public IReadOnlyCollection<ChatMessage> Messages => _messages.AsReadOnly();

    private Conversation()
    {
    }

    public static Conversation Create(Guid projectId, Guid agentId)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project id is required.", nameof(projectId));
        }

        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("Agent id is required.", nameof(agentId));
        }

        return new Conversation
        {
            ProjectId = projectId,
            AgentId = agentId,
        };
    }

    public ChatMessage AddMessage(
        ChatMessageRole role,
        string content,
        string? proposedTicketTitle = null,
        string? proposedTicketDescription = null,
        string? senderName = null)
    {
        var message = ChatMessage.Create(Id, role, content, proposedTicketTitle, proposedTicketDescription, senderName);
        _messages.Add(message);
        MarkUpdated();

        return message;
    }
}
