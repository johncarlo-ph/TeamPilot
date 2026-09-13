using Microsoft.AspNetCore.Mvc;
using TeamPilot.Application.LiveAgentChat;
using TeamPilot.Application.LiveAgentChat.Dtos;

namespace TeamPilot.API.Controllers;

[ApiController]
public class LiveAgentChatController(ILiveAgentChatService chatService) : ControllerBase
{
    [HttpGet("api/projects/{projectId:guid}/live-agent/messages")]
    public async Task<ActionResult<IReadOnlyList<ChatMessageDto>>> GetHistory(Guid projectId, CancellationToken cancellationToken)
    {
        var messages = await chatService.GetHistoryAsync(projectId, cancellationToken);
        return Ok(messages);
    }

    [HttpPost("api/projects/{projectId:guid}/live-agent/messages")]
    public async Task<ActionResult<ChatMessageDto>> SendMessage(Guid projectId, [FromBody] SendChatMessageRequest request, CancellationToken cancellationToken)
    {
        var message = await chatService.SendMessageAsync(projectId, request, cancellationToken);
        return Ok(message);
    }

    [HttpPost("api/projects/{projectId:guid}/live-agent/messages/{messageId:guid}/approve-ticket")]
    public async Task<ActionResult<ChatMessageDto>> ApproveTicket(Guid projectId, Guid messageId, CancellationToken cancellationToken)
    {
        var message = await chatService.ApproveTicketAsync(projectId, messageId, cancellationToken);
        return Ok(message);
    }

    [HttpPost("api/projects/{projectId:guid}/live-agent/messages/{messageId:guid}/reject-ticket")]
    public async Task<ActionResult<ChatMessageDto>> RejectTicket(Guid projectId, Guid messageId, CancellationToken cancellationToken)
    {
        var message = await chatService.RejectTicketAsync(projectId, messageId, cancellationToken);
        return Ok(message);
    }
}
