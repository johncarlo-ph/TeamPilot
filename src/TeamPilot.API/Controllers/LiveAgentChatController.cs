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
}
