using Microsoft.AspNetCore.Mvc;
using TeamPilot.Application.Orchestration;
using TeamPilot.Application.TicketQuestions;
using TeamPilot.Application.Tickets;
using TeamPilot.Application.Tickets.Dtos;
using TeamPilot.Domain.Enums;

namespace TeamPilot.API.Controllers;

[ApiController]
public class TicketsController(ITicketService ticketService, IOrchestrationService orchestrationService, ITicketQuestionService ticketQuestionService) : ControllerBase
{
    [HttpPost("api/sprints/{sprintId:guid}/tickets")]
    public async Task<ActionResult<TicketDto>> Create(Guid sprintId, [FromBody] CreateTicketRequest request, CancellationToken cancellationToken)
    {
        var ticket = await ticketService.CreateAsync(sprintId, request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = ticket.Id }, ticket);
    }

    [HttpGet("api/sprints/{sprintId:guid}/tickets")]
    public async Task<ActionResult<IReadOnlyList<TicketDto>>> List(Guid sprintId, [FromQuery] TicketStatus? status, CancellationToken cancellationToken)
    {
        var tickets = await ticketService.ListAsync(sprintId, status, cancellationToken);
        return Ok(tickets);
    }

    [HttpGet("api/projects/{projectId:guid}/tickets")]
    public async Task<ActionResult<IReadOnlyList<TicketDto>>> ListByProject(Guid projectId, [FromQuery] TicketStatus? status, CancellationToken cancellationToken)
    {
        var tickets = await ticketService.ListByProjectAsync(projectId, status, cancellationToken);
        return Ok(tickets);
    }

    [HttpGet("api/tickets/{id:guid}")]
    public async Task<ActionResult<TicketDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var ticket = await ticketService.GetByIdAsync(id, cancellationToken);
        return Ok(ticket);
    }

    [HttpPost("api/tickets/{id:guid}/start")]
    public async Task<ActionResult<TicketDto>> Start(Guid id, CancellationToken cancellationToken)
    {
        var ticket = await orchestrationService.StartPipelineAsync(id, cancellationToken);
        return Ok(ticket);
    }

    [HttpPost("api/tickets/{id:guid}/move-to-review")]
    public async Task<ActionResult<TicketDto>> MoveToReview(Guid id, CancellationToken cancellationToken)
    {
        var ticket = await ticketService.MoveToReviewAsync(id, cancellationToken);
        return Ok(ticket);
    }

    [HttpPost("api/tickets/{id:guid}/cancel")]
    public async Task<ActionResult<TicketDto>> Cancel(Guid id, [FromBody] CancelTicketRequest request, CancellationToken cancellationToken)
    {
        var ticket = await ticketService.CancelAsync(id, request, cancellationToken);
        return Ok(ticket);
    }

    [HttpPost("api/tickets/{id:guid}/retry")]
    public async Task<ActionResult<TicketDto>> Retry(Guid id, CancellationToken cancellationToken)
    {
        var ticket = await ticketQuestionService.RetryAsync(id, cancellationToken);
        return Ok(ticket);
    }
}
