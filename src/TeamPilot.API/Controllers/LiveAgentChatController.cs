using Microsoft.AspNetCore.Mvc;
using TeamPilot.Application.LiveAgentChat;
using TeamPilot.Application.LiveAgentChat.Dtos;

namespace TeamPilot.API.Controllers;

[ApiController]
public class LiveAgentChatController(ILiveAgentChatService chatService) : ControllerBase
{
    [HttpGet("api/projects/{projectId:guid}/live-agent/conversations")]
    public async Task<ActionResult<IReadOnlyList<ConversationDto>>> ListConversations(Guid projectId, CancellationToken cancellationToken)
    {
        var conversations = await chatService.ListConversationsAsync(projectId, cancellationToken);
        return Ok(conversations);
    }

    [HttpPost("api/projects/{projectId:guid}/live-agent/conversations")]
    public async Task<ActionResult<ConversationDto>> CreateConversation(Guid projectId, [FromBody] CreateConversationRequest request, CancellationToken cancellationToken)
    {
        var conversation = await chatService.CreateConversationAsync(projectId, request, cancellationToken);
        return Ok(conversation);
    }

    [HttpPut("api/projects/{projectId:guid}/live-agent/conversations/{conversationId:guid}")]
    public async Task<ActionResult<ConversationDto>> RenameConversation(Guid projectId, Guid conversationId, [FromBody] RenameConversationRequest request, CancellationToken cancellationToken)
    {
        var conversation = await chatService.RenameConversationAsync(projectId, conversationId, request, cancellationToken);
        return Ok(conversation);
    }

    [HttpGet("api/projects/{projectId:guid}/live-agent/conversations/{conversationId:guid}/messages")]
    public async Task<ActionResult<IReadOnlyList<ChatMessageDto>>> GetHistory(Guid projectId, Guid conversationId, CancellationToken cancellationToken)
    {
        var messages = await chatService.GetHistoryAsync(projectId, conversationId, cancellationToken);
        return Ok(messages);
    }

    [HttpPost("api/projects/{projectId:guid}/live-agent/conversations/{conversationId:guid}/messages")]
    public async Task<ActionResult<ChatMessageDto>> SendMessage(Guid projectId, Guid conversationId, [FromBody] SendChatMessageRequest request, CancellationToken cancellationToken)
    {
        var message = await chatService.SendMessageAsync(projectId, conversationId, request, cancellationToken);
        return Ok(message);
    }

    [HttpPost("api/projects/{projectId:guid}/live-agent/conversations/{conversationId:guid}/messages/{messageId:guid}/approve-ticket")]
    public async Task<ActionResult<ChatMessageDto>> ApproveTicket(Guid projectId, Guid conversationId, Guid messageId, CancellationToken cancellationToken)
    {
        var message = await chatService.ApproveTicketAsync(projectId, conversationId, messageId, cancellationToken);
        return Ok(message);
    }

    [HttpPost("api/projects/{projectId:guid}/live-agent/conversations/{conversationId:guid}/messages/{messageId:guid}/reject-ticket")]
    public async Task<ActionResult<ChatMessageDto>> RejectTicket(Guid projectId, Guid conversationId, Guid messageId, CancellationToken cancellationToken)
    {
        var message = await chatService.RejectTicketAsync(projectId, conversationId, messageId, cancellationToken);
        return Ok(message);
    }
}
