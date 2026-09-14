using Moq;
using TeamPilot.Application.Agents;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Git;
using TeamPilot.Application.Instructions;
using TeamPilot.Application.LiveAgentChat;
using TeamPilot.Application.LiveAgentChat.Dtos;
using TeamPilot.Application.LiveAgentChat.Validators;
using TeamPilot.Application.Llm;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Tickets;
using TeamPilot.Application.Tickets.Dtos;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using TeamPilot.Domain.Exceptions;
using Xunit;

namespace TeamPilot.Application.Tests.LiveAgentChat;

public class LiveAgentChatServiceTests
{
    private readonly Mock<IConversationRepository> _conversationRepository = new();
    private readonly Mock<IAgentRepository> _agentRepository = new();
    private readonly Mock<IAgentService> _agentService = new();
    private readonly Mock<IInstructionRepository> _instructionRepository = new();
    private readonly Mock<IProjectRepository> _projectRepository = new();
    private readonly Mock<ITicketRepository> _ticketRepository = new();
    private readonly Mock<ITicketService> _ticketService = new();
    private readonly Mock<IGitService> _gitService = new();
    private readonly Mock<ILlmConnector> _llmConnector = new();
    private readonly Mock<IProjectAccessGuard> _projectAccessGuard = new();
    private readonly Mock<ICurrentUserContext> _currentUser = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly LiveAgentChatService _sut;

    private readonly Project _project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main");
    private readonly Agent _liveAgent;
    private readonly Conversation _conversation;
    private readonly Guid _currentUserId = Guid.NewGuid();

    public LiveAgentChatServiceTests()
    {
        _liveAgent = Agent.Create(Guid.NewGuid(), "Live Agent", AgentRole.LiveAgent);
        _conversation = Conversation.Create(_project.Id, _liveAgent.Id, "General", _currentUserId, "Jordan Lee");

        _projectRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(_project);
        _agentService.Setup(s => s.EnsureLiveAgentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _agentRepository
            .Setup(r => r.GetByProjectAndRoleAsync(It.IsAny<Guid>(), AgentRole.LiveAgent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_liveAgent);
        _conversationRepository.Setup(r => r.GetByIdAsync(_conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_conversation);
        _currentUser.Setup(u => u.Name).Returns("Jordan Lee");
        _currentUser.Setup(u => u.UserId).Returns(_currentUserId);

        _sut = new LiveAgentChatService(
            _conversationRepository.Object,
            _agentRepository.Object,
            _agentService.Object,
            _instructionRepository.Object,
            _projectRepository.Object,
            _ticketRepository.Object,
            _ticketService.Object,
            _gitService.Object,
            _llmConnector.Object,
            _projectAccessGuard.Object,
            _currentUser.Object,
            _unitOfWork.Object,
            new SendChatMessageRequestValidator(),
            new CreateConversationRequestValidator(),
            new RenameConversationRequestValidator(),
            new ApproveTicketRequestValidator());
    }

    private static LlmConversationResponse FinalTextResponse(string text) =>
        new([new LlmTextBlock(text)], "end_turn", "claude-test", 10, 20);

    [Fact]
    public async Task ListConversationsAsync_ReturnsProjectsConversations()
    {
        _conversationRepository
            .Setup(r => r.ListByProjectIdAsync(_project.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_conversation]);

        var conversations = await _sut.ListConversationsAsync(_project.Id);

        var summary = Assert.Single(conversations);
        Assert.Equal(_conversation.Id, summary.Id);
        Assert.Equal("General", summary.Title);
        Assert.Equal("Jordan Lee", summary.CreatedByName);
    }

    [Fact]
    public async Task CreateConversationAsync_WithTitle_CreatesConversationForCurrentUser()
    {
        Conversation? captured = null;
        _conversationRepository
            .Setup(r => r.AddAsync(It.IsAny<Conversation>(), It.IsAny<CancellationToken>()))
            .Callback<Conversation, CancellationToken>((conversation, _) => captured = conversation)
            .Returns(Task.CompletedTask);

        var result = await _sut.CreateConversationAsync(_project.Id, new CreateConversationRequest("Bug triage"));

        Assert.Equal("Bug triage", result.Title);
        Assert.Equal(_currentUserId, result.CreatedByUserId);
        Assert.Equal("Jordan Lee", result.CreatedByName);
        Assert.Equal("Bug triage", captured!.Title);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateConversationAsync_NoTitle_FallsBackToDefaultTitle()
    {
        var result = await _sut.CreateConversationAsync(_project.Id, new CreateConversationRequest(null));

        Assert.Equal(Conversation.DefaultTitle, result.Title);
    }

    [Fact]
    public async Task RenameConversationAsync_UpdatesTitle()
    {
        var result = await _sut.RenameConversationAsync(_project.Id, _conversation.Id, new RenameConversationRequest("Renamed session"));

        Assert.Equal("Renamed session", result.Title);
        Assert.Equal("Renamed session", _conversation.Title);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RenameConversationAsync_BlankTitle_ThrowsValidationException()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => _sut.RenameConversationAsync(_project.Id, _conversation.Id, new RenameConversationRequest(" ")));
    }

    [Fact]
    public async Task RenameConversationAsync_ConversationBelongsToAnotherProject_ThrowsNotFoundException()
    {
        var otherProjectConversation = Conversation.Create(Guid.NewGuid(), _liveAgent.Id);
        _conversationRepository.Setup(r => r.GetByIdAsync(otherProjectConversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(otherProjectConversation);

        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.RenameConversationAsync(_project.Id, otherProjectConversation.Id, new RenameConversationRequest("Renamed")));
    }

    [Fact]
    public async Task RenameConversationAsync_ConversationNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.RenameConversationAsync(_project.Id, Guid.NewGuid(), new RenameConversationRequest("Renamed")));
    }

    [Fact]
    public async Task GetHistoryAsync_ConversationNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetHistoryAsync(_project.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task GetHistoryAsync_ExistingConversation_ReturnsItsMessages()
    {
        _conversation.AddMessage(ChatMessageRole.User, "Hello", senderName: "Jordan Lee");

        var history = await _sut.GetHistoryAsync(_project.Id, _conversation.Id);

        var message = Assert.Single(history);
        Assert.Equal("Hello", message.Content);
    }

    [Fact]
    public async Task SendMessageAsync_NoToolCallNeeded_PersistsUserAndAssistantMessages()
    {
        _llmConnector
            .Setup(l => l.SendConversationAsync(It.IsAny<LlmConversationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FinalTextResponse("This project tracks tickets through an AI pipeline."));

        var reply = await _sut.SendMessageAsync(_project.Id, _conversation.Id, new SendChatMessageRequest("What does this project do?"));

        Assert.Equal(ChatMessageRole.Assistant, reply.Role);
        Assert.Equal("This project tracks tickets through an AI pipeline.", reply.Content);
        Assert.Null(reply.ProposedTicketTitle);
        Assert.Equal(2, _conversation.Messages.Count);
        _gitService.Verify(g => g.ReadFileAsync(It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendMessageAsync_ConversationNotFound_ThrowsNotFoundException()
    {
        _llmConnector
            .Setup(l => l.SendConversationAsync(It.IsAny<LlmConversationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FinalTextResponse("irrelevant"));

        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.SendMessageAsync(_project.Id, Guid.NewGuid(), new SendChatMessageRequest("Hello?")));
    }

    [Fact]
    public async Task SendMessageAsync_NoToolCallNeeded_StampsUserMessageWithSenderName()
    {
        _llmConnector
            .Setup(l => l.SendConversationAsync(It.IsAny<LlmConversationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FinalTextResponse("This project tracks tickets through an AI pipeline."));

        await _sut.SendMessageAsync(_project.Id, _conversation.Id, new SendChatMessageRequest("What does this project do?"));

        var userMessage = Assert.Single(_conversation.Messages, m => m.Role == ChatMessageRole.User);
        Assert.Equal("Jordan Lee", userMessage.SenderName);
    }

    [Fact]
    public async Task SendMessageAsync_ModelReadsAFile_ExecutesToolAndReturnsFinalAnswer()
    {
        var toolUseResponse = new LlmConversationResponse(
            [new LlmToolUseBlock("call-1", "read_file", """{"path":"README.md"}""")],
            "tool_use",
            "claude-test",
            10,
            20);

        _gitService
            .Setup(g => g.ReadFileAsync(_project.RepositoryPath, "README.md", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitFileReadResult(true, "# TeamPilot\nAn AI ticketing system.", false));

        _llmConnector
            .SetupSequence(l => l.SendConversationAsync(It.IsAny<LlmConversationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(toolUseResponse)
            .ReturnsAsync(FinalTextResponse("README.md says it's an AI ticketing system."));

        var reply = await _sut.SendMessageAsync(_project.Id, _conversation.Id, new SendChatMessageRequest("Explain the README."));

        Assert.Equal("README.md says it's an AI ticketing system.", reply.Content);
        _gitService.Verify(g => g.ReadFileAsync(_project.RepositoryPath, "README.md", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_ModelReadsATicket_ExecutesGetTicketAndReturnsFinalAnswer()
    {
        var ticket = Ticket.Create(_project.Id, "Fix login bug", "Users can't sign in with Google.");

        var toolUseResponse = new LlmConversationResponse(
            [new LlmToolUseBlock("call-1", "get_ticket", $$"""{"id":"{{ticket.Id}}"}""")],
            "tool_use",
            "claude-test",
            10,
            20);

        _ticketRepository
            .Setup(t => t.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        _llmConnector
            .SetupSequence(l => l.SendConversationAsync(It.IsAny<LlmConversationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(toolUseResponse)
            .ReturnsAsync(FinalTextResponse("That ticket is about a Google sign-in bug."));

        var reply = await _sut.SendMessageAsync(_project.Id, _conversation.Id, new SendChatMessageRequest("What is the login bug ticket about?"));

        Assert.Equal("That ticket is about a Google sign-in bug.", reply.Content);
        _ticketRepository.Verify(t => t.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_ModelReadsATicketFromAnotherProject_ReturnsNotFoundWithoutLeakingIt()
    {
        var otherProjectTicket = Ticket.Create(Guid.NewGuid(), "Unrelated ticket", "Belongs to a different project.");

        var toolUseResponse = new LlmConversationResponse(
            [new LlmToolUseBlock("call-1", "get_ticket", $$"""{"id":"{{otherProjectTicket.Id}}"}""")],
            "tool_use",
            "claude-test",
            10,
            20);

        _ticketRepository
            .Setup(t => t.GetByIdAsync(otherProjectTicket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(otherProjectTicket);

        _llmConnector
            .SetupSequence(l => l.SendConversationAsync(It.IsAny<LlmConversationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(toolUseResponse)
            .ReturnsAsync(FinalTextResponse("I couldn't find that ticket."));

        var reply = await _sut.SendMessageAsync(_project.Id, _conversation.Id, new SendChatMessageRequest($"Tell me about ticket {otherProjectTicket.Id}"));

        Assert.Equal("I couldn't find that ticket.", reply.Content);
    }

    [Fact]
    public async Task SendMessageAsync_ModelDraftsATicket_CapturesProposalWithoutCreatingATicket()
    {
        var proposeResponse = new LlmConversationResponse(
            [new LlmToolUseBlock("call-1", "propose_ticket", """{"title":"Fix login bug","description":"Users can't sign in with Google."}""")],
            "tool_use",
            "claude-test",
            10,
            20);

        _llmConnector
            .SetupSequence(l => l.SendConversationAsync(It.IsAny<LlmConversationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposeResponse)
            .ReturnsAsync(FinalTextResponse("I've drafted a ticket for you to review."));

        var reply = await _sut.SendMessageAsync(_project.Id, _conversation.Id, new SendChatMessageRequest("Please create a ticket for the login bug."));

        Assert.Equal("Fix login bug", reply.ProposedTicketTitle);
        Assert.Equal("Users can't sign in with Google.", reply.ProposedTicketDescription);
        Assert.Null(reply.CreatedTicketId);
        _ticketRepository.Verify(t => t.AddAsync(It.IsAny<Ticket>(), It.IsAny<CancellationToken>()), Times.Never);
        _ticketService.Verify(t => t.CreateAsync(It.IsAny<Guid>(), It.IsAny<CreateTicketRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApproveTicketAsync_MessageWithProposal_CreatesTicketAndStampsMessage()
    {
        var message = _conversation.AddMessage(
            ChatMessageRole.Assistant,
            "Here's a draft ticket.",
            "Fix login bug",
            "Users can't sign in with Google.");

        var createdTicket = new TicketDto(
            Guid.NewGuid(), _project.Id, "Fix login bug", "Users can't sign in with Google.",
            TicketStatus.ToDo, null, null, DateTime.UtcNow, null);
        _ticketService
            .Setup(t => t.CreateAsync(
                _project.Id,
                It.Is<CreateTicketRequest>(r => r.Title == "Fix login bug" && r.Description == "Users can't sign in with Google."),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdTicket);

        var result = await _sut.ApproveTicketAsync(
            _project.Id, _conversation.Id, message.Id, new ApproveTicketRequest("Fix login bug", "Users can't sign in with Google."));

        Assert.Equal(createdTicket.Id, result.CreatedTicketId);
        Assert.Equal(createdTicket.Id, message.CreatedTicketId);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApproveTicketAsync_EditedTitleAndDescription_CreatesTicketWithEditedValues()
    {
        var message = _conversation.AddMessage(
            ChatMessageRole.Assistant,
            "Here's a draft ticket.",
            "Fix login bug",
            "Users can't sign in with Google.");

        var createdTicket = new TicketDto(
            Guid.NewGuid(), _project.Id, "Fix Google login bug", "Edited description",
            TicketStatus.ToDo, null, null, DateTime.UtcNow, null);
        _ticketService
            .Setup(t => t.CreateAsync(
                _project.Id,
                It.Is<CreateTicketRequest>(r => r.Title == "Fix Google login bug" && r.Description == "Edited description"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdTicket);

        var result = await _sut.ApproveTicketAsync(
            _project.Id, _conversation.Id, message.Id, new ApproveTicketRequest("Fix Google login bug", "Edited description"));

        Assert.Equal("Fix Google login bug", result.ProposedTicketTitle);
        Assert.Equal("Edited description", result.ProposedTicketDescription);
        Assert.Equal(createdTicket.Id, result.CreatedTicketId);
    }

    [Fact]
    public async Task ApproveTicketAsync_BlankTitle_ThrowsValidationException()
    {
        var message = _conversation.AddMessage(ChatMessageRole.Assistant, "Here's a draft ticket.", "Fix login bug", "desc");

        await Assert.ThrowsAnyAsync<Exception>(
            () => _sut.ApproveTicketAsync(_project.Id, _conversation.Id, message.Id, new ApproveTicketRequest(" ", "desc")));
        _ticketService.Verify(t => t.CreateAsync(It.IsAny<Guid>(), It.IsAny<CreateTicketRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApproveTicketAsync_AlreadyApproved_ReturnsExistingStateWithoutCreatingAnotherTicket()
    {
        var message = _conversation.AddMessage(ChatMessageRole.Assistant, "Here's a draft ticket.", "Fix login bug", "desc");
        var existingTicketId = Guid.NewGuid();
        message.MarkTicketCreated(existingTicketId, "Fix login bug", "desc");

        var result = await _sut.ApproveTicketAsync(_project.Id, _conversation.Id, message.Id, new ApproveTicketRequest("Fix login bug", "desc"));

        Assert.Equal(existingTicketId, result.CreatedTicketId);
        _ticketService.Verify(t => t.CreateAsync(It.IsAny<Guid>(), It.IsAny<CreateTicketRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApproveTicketAsync_MessageNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.ApproveTicketAsync(_project.Id, _conversation.Id, Guid.NewGuid(), new ApproveTicketRequest("Fix login bug", "desc")));
    }

    [Fact]
    public async Task ApproveTicketAsync_AlreadyRejected_ThrowsChatMessageTicketApprovalException()
    {
        var message = _conversation.AddMessage(ChatMessageRole.Assistant, "Here's a draft ticket.", "Fix login bug", "desc");
        message.RejectTicket();

        await Assert.ThrowsAsync<ChatMessageTicketApprovalException>(
            () => _sut.ApproveTicketAsync(_project.Id, _conversation.Id, message.Id, new ApproveTicketRequest("Fix login bug", "desc")));
        _ticketService.Verify(t => t.CreateAsync(It.IsAny<Guid>(), It.IsAny<CreateTicketRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RejectTicketAsync_MessageWithProposal_MarksMessageRejected()
    {
        var message = _conversation.AddMessage(ChatMessageRole.Assistant, "Here's a draft ticket.", "Fix login bug", "desc");

        var result = await _sut.RejectTicketAsync(_project.Id, _conversation.Id, message.Id);

        Assert.True(result.TicketRejected);
        Assert.True(message.TicketRejected);
        Assert.Null(result.CreatedTicketId);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RejectTicketAsync_AlreadyRejected_ReturnsExistingStateWithoutThrowing()
    {
        var message = _conversation.AddMessage(ChatMessageRole.Assistant, "Here's a draft ticket.", "Fix login bug", "desc");
        message.RejectTicket();

        var result = await _sut.RejectTicketAsync(_project.Id, _conversation.Id, message.Id);

        Assert.True(result.TicketRejected);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RejectTicketAsync_AlreadyApproved_ThrowsChatMessageTicketApprovalException()
    {
        var message = _conversation.AddMessage(ChatMessageRole.Assistant, "Here's a draft ticket.", "Fix login bug", "desc");
        message.MarkTicketCreated(Guid.NewGuid(), "Fix login bug", "desc");

        await Assert.ThrowsAsync<ChatMessageTicketApprovalException>(() => _sut.RejectTicketAsync(_project.Id, _conversation.Id, message.Id));
    }

    [Fact]
    public async Task RejectTicketAsync_MessageNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.RejectTicketAsync(_project.Id, _conversation.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task SendMessageAsync_ReadFileOutsideSandbox_SurfacesGitOperationExceptionAsToolErrorWithoutThrowing()
    {
        var toolUseResponse = new LlmConversationResponse(
            [new LlmToolUseBlock("call-1", "read_file", """{"path":"../../secrets.txt"}""")],
            "tool_use",
            "claude-test",
            10,
            20);

        _gitService
            .Setup(g => g.ReadFileAsync(_project.RepositoryPath, "../../secrets.txt", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GitOperationException("Path is outside the repository sandbox."));

        _llmConnector
            .SetupSequence(l => l.SendConversationAsync(It.IsAny<LlmConversationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(toolUseResponse)
            .ReturnsAsync(FinalTextResponse("I couldn't access that file."));

        var reply = await _sut.SendMessageAsync(_project.Id, _conversation.Id, new SendChatMessageRequest("Read ../../secrets.txt"));

        Assert.Equal("I couldn't access that file.", reply.Content);
    }
}
