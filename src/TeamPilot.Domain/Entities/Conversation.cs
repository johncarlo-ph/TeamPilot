using TeamPilot.Domain.Common;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Domain.Entities;

/// <summary>
/// One chat thread with a project's Live Agent (see <see cref="AgentRole.LiveAgent"/>). A project
/// can have any number of these - each user can start their own, and every project member can see
/// and select from the full list (see <see cref="TeamPilot.Application.LiveAgentChat.ILiveAgentChatService"/>).
/// </summary>
public class Conversation : Entity
{
    public const string DefaultTitle = "New chat";

    private readonly List<ChatMessage> _messages = [];

    public Guid ProjectId { get; private set; }

    public Guid AgentId { get; private set; }

    public string Title { get; private set; } = DefaultTitle;

    /// <summary>
    /// Id of the user who started this conversation. Null for a conversation created before this
    /// field existed - never null for one created going forward.
    /// </summary>
    public Guid? CreatedByUserId { get; private set; }

    /// <summary>
    /// Display name of the user who started this conversation, captured at creation time so it
    /// keeps showing correctly even if that user is later renamed or removed - same reasoning as
    /// <see cref="ChatMessage.SenderName"/>.
    /// </summary>
    public string? CreatedByName { get; private set; }

    public IReadOnlyCollection<ChatMessage> Messages => _messages.AsReadOnly();

    private Conversation()
    {
    }

    public static Conversation Create(Guid projectId, Guid agentId, string? title = null, Guid? createdByUserId = null, string? createdByName = null)
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
            Title = string.IsNullOrWhiteSpace(title) ? DefaultTitle : title.Trim(),
            CreatedByUserId = createdByUserId is null || createdByUserId == Guid.Empty ? null : createdByUserId,
            CreatedByName = string.IsNullOrWhiteSpace(createdByName) ? null : createdByName.Trim(),
        };
    }

    public void Rename(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Conversation title is required.", nameof(title));
        }

        Title = title.Trim();
        MarkUpdated();
    }

    public ChatMessage AddMessage(
        ChatMessageRole role,
        string content,
        string? proposedTicketTitle = null,
        string? proposedTicketDescription = null,
        string? proposedTicketAcceptanceCriteria = null,
        string? senderName = null)
    {
        var message = ChatMessage.Create(Id, role, content, proposedTicketTitle, proposedTicketDescription, proposedTicketAcceptanceCriteria, senderName);
        _messages.Add(message);
        MarkUpdated();

        return message;
    }
}
