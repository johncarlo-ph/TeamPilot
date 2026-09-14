using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using TeamPilot.Domain.Exceptions;
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
    public void Create_NoTitle_FallsBackToDefaultTitle()
    {
        var conversation = Conversation.Create(ProjectId, AgentId);

        Assert.Equal(Conversation.DefaultTitle, conversation.Title);
    }

    [Fact]
    public void Create_BlankTitle_FallsBackToDefaultTitle()
    {
        var conversation = Conversation.Create(ProjectId, AgentId, "   ");

        Assert.Equal(Conversation.DefaultTitle, conversation.Title);
    }

    [Fact]
    public void Create_WithTitleAndCreator_CapturesThemTrimmed()
    {
        var userId = Guid.NewGuid();

        var conversation = Conversation.Create(ProjectId, AgentId, "  Bug triage  ", userId, "  Jordan Lee  ");

        Assert.Equal("Bug triage", conversation.Title);
        Assert.Equal(userId, conversation.CreatedByUserId);
        Assert.Equal("Jordan Lee", conversation.CreatedByName);
    }

    [Fact]
    public void Create_EmptyCreatedByUserId_StoresNull()
    {
        var conversation = Conversation.Create(ProjectId, AgentId, createdByUserId: Guid.Empty);

        Assert.Null(conversation.CreatedByUserId);
    }

    [Fact]
    public void Rename_NewTitle_UpdatesTitleTrimmed()
    {
        var conversation = Conversation.Create(ProjectId, AgentId);

        conversation.Rename("  Renamed session  ");

        Assert.Equal("Renamed session", conversation.Title);
    }

    [Fact]
    public void Rename_BlankTitle_ThrowsArgumentException()
    {
        var conversation = Conversation.Create(ProjectId, AgentId);

        Assert.Throws<ArgumentException>(() => conversation.Rename("   "));
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
    public void AddMessage_UserMessageWithSenderName_CarriesSenderName()
    {
        var conversation = Conversation.Create(ProjectId, AgentId);

        var message = conversation.AddMessage(ChatMessageRole.User, "What does this project do?", senderName: "Jordan Lee");

        Assert.Equal("Jordan Lee", message.SenderName);
    }

    [Fact]
    public void AddMessage_EmptyContent_ThrowsArgumentException()
    {
        var conversation = Conversation.Create(ProjectId, AgentId);

        Assert.Throws<ArgumentException>(() => conversation.AddMessage(ChatMessageRole.User, "   "));
    }

    [Fact]
    public void MarkTicketCreated_MessageWithProposal_SetsCreatedTicketId()
    {
        var conversation = Conversation.Create(ProjectId, AgentId);
        var message = conversation.AddMessage(ChatMessageRole.Assistant, "Here's a draft ticket.", "Fix login bug", "desc");
        var ticketId = Guid.NewGuid();

        message.MarkTicketCreated(ticketId, "Fix login bug", "desc");

        Assert.Equal(ticketId, message.CreatedTicketId);
    }

    [Fact]
    public void MarkTicketCreated_EditedTitleAndDescription_OverwritesProposedValues()
    {
        var conversation = Conversation.Create(ProjectId, AgentId);
        var message = conversation.AddMessage(ChatMessageRole.Assistant, "Here's a draft ticket.", "Fix login bug", "desc");

        message.MarkTicketCreated(Guid.NewGuid(), "Fix Google login bug", "Edited description");

        Assert.Equal("Fix Google login bug", message.ProposedTicketTitle);
        Assert.Equal("Edited description", message.ProposedTicketDescription);
    }

    [Fact]
    public void MarkTicketCreated_AlreadyApproved_ThrowsChatMessageTicketApprovalException()
    {
        var conversation = Conversation.Create(ProjectId, AgentId);
        var message = conversation.AddMessage(ChatMessageRole.Assistant, "Here's a draft ticket.", "Fix login bug", "desc");
        message.MarkTicketCreated(Guid.NewGuid(), "Fix login bug", "desc");

        Assert.Throws<ChatMessageTicketApprovalException>(() => message.MarkTicketCreated(Guid.NewGuid(), "Fix login bug", "desc"));
    }

    [Fact]
    public void MarkTicketCreated_MessageWithNoProposal_ThrowsChatMessageTicketApprovalException()
    {
        var conversation = Conversation.Create(ProjectId, AgentId);
        var message = conversation.AddMessage(ChatMessageRole.User, "What does this project do?");

        Assert.Throws<ChatMessageTicketApprovalException>(() => message.MarkTicketCreated(Guid.NewGuid(), "Fix login bug", "desc"));
    }

    [Fact]
    public void MarkTicketCreated_AlreadyRejected_ThrowsChatMessageTicketApprovalException()
    {
        var conversation = Conversation.Create(ProjectId, AgentId);
        var message = conversation.AddMessage(ChatMessageRole.Assistant, "Here's a draft ticket.", "Fix login bug", "desc");
        message.RejectTicket();

        Assert.Throws<ChatMessageTicketApprovalException>(() => message.MarkTicketCreated(Guid.NewGuid(), "Fix login bug", "desc"));
    }

    [Fact]
    public void RejectTicket_MessageWithProposal_SetsTicketRejected()
    {
        var conversation = Conversation.Create(ProjectId, AgentId);
        var message = conversation.AddMessage(ChatMessageRole.Assistant, "Here's a draft ticket.", "Fix login bug", "desc");

        message.RejectTicket();

        Assert.True(message.TicketRejected);
    }

    [Fact]
    public void RejectTicket_AlreadyRejected_ThrowsChatMessageTicketApprovalException()
    {
        var conversation = Conversation.Create(ProjectId, AgentId);
        var message = conversation.AddMessage(ChatMessageRole.Assistant, "Here's a draft ticket.", "Fix login bug", "desc");
        message.RejectTicket();

        Assert.Throws<ChatMessageTicketApprovalException>(() => message.RejectTicket());
    }

    [Fact]
    public void RejectTicket_AlreadyApproved_ThrowsChatMessageTicketApprovalException()
    {
        var conversation = Conversation.Create(ProjectId, AgentId);
        var message = conversation.AddMessage(ChatMessageRole.Assistant, "Here's a draft ticket.", "Fix login bug", "desc");
        message.MarkTicketCreated(Guid.NewGuid(), "Fix login bug", "desc");

        Assert.Throws<ChatMessageTicketApprovalException>(() => message.RejectTicket());
    }

    [Fact]
    public void RejectTicket_MessageWithNoProposal_ThrowsChatMessageTicketApprovalException()
    {
        var conversation = Conversation.Create(ProjectId, AgentId);
        var message = conversation.AddMessage(ChatMessageRole.User, "What does this project do?");

        Assert.Throws<ChatMessageTicketApprovalException>(() => message.RejectTicket());
    }
}
