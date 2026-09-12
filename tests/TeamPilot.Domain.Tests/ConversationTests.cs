using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using Xunit;

namespace TeamPilot.Domain.Tests;

public class ConversationTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid AgentId = Guid.NewGuid();

    [Fact]
    public void Create_WithEmptyProjectId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Conversation.Create(Guid.Empty, AgentId));
    }

    [Fact]
    public void Create_WithEmptyAgentId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Conversation.Create(ProjectId, Guid.Empty));
    }

    [Fact]
    public void AddMessage_UserMessage_AppendsWithNoProposedTicket()
    {
        var conversation = Conversation.Create(ProjectId, AgentId);

        var message = conversation.AddMessage(ChatMessageRole.User, "What does this project do?");

        var stored = Assert.Single(conversation.Messages);
        Assert.Same(message, stored);
        Assert.Equal(ChatMessageRole.User, stored.Role);
        Assert.Equal("What does this project do?", stored.Content);
        Assert.Null(stored.ProposedTicketTitle);
        Assert.Null(stored.ProposedTicketDescription);
    }

    [Fact]
    public void AddMessage_AssistantMessageWithProposedTicket_CarriesDraftFields()
    {
        var conversation = Conversation.Create(ProjectId, AgentId);

        var message = conversation.AddMessage(
            ChatMessageRole.Assistant,
            "Here's a draft ticket.",
            "Fix login bug",
            "Users can't sign in with Google.");

        Assert.Equal("Fix login bug", message.ProposedTicketTitle);
        Assert.Equal("Users can't sign in with Google.", message.ProposedTicketDescription);
    }

    [Fact]
    public void AddMessage_MultipleMessages_PreservesInsertionOrder()
    {
        var conversation = Conversation.Create(ProjectId, AgentId);

        conversation.AddMessage(ChatMessageRole.User, "First");
        conversation.AddMessage(ChatMessageRole.Assistant, "Second");
        conversation.AddMessage(ChatMessageRole.User, "Third");

        Assert.Equal(["First", "Second", "Third"], conversation.Messages.Select(m => m.Content));
    }

    [Fact]
    public void AddMessage_EmptyContent_ThrowsArgumentException()
    {
        var conversation = Conversation.Create(ProjectId, AgentId);

        Assert.Throws<ArgumentException>(() => conversation.AddMessage(ChatMessageRole.User, "   "));
    }
}
