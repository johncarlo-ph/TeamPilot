using Microsoft.AspNetCore.Mvc;
using TeamPilot.Application.TicketAgentEvents;
using TeamPilot.Application.TicketAgentEvents.Dtos;
using TeamPilot.Domain.Entities;

namespace TeamPilot.API.Controllers;

[ApiController]
[Route("api/tickets/{ticketId:guid}/agent-events")]
public class TicketAgentEventsController(ITicketAgentEventService ticketAgentEventService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TicketAgentEventDto>>> ListByTicket(Guid ticketId, CancellationToken cancellationToken)
    {
        var events = await ticketAgentEventService.ListByTicketAsync(ticketId, cancellationToken);
        return Ok(events.Select(ToDto));
    }

    private static TicketAgentEventDto ToDto(TicketAgentEvent agentEvent) => new(
        agentEvent.Id,
        agentEvent.TicketId,
        agentEvent.AgentId,
        agentEvent.Role,
        agentEvent.Kind,
        agentEvent.Result,
        agentEvent.InputTokens,
        agentEvent.OutputTokens,
        agentEvent.DurationMs,
        agentEvent.CreatedAtUtc);
}
