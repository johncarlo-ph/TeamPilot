using Microsoft.AspNetCore.Mvc;
using TeamPilot.Application.TicketQuestions;
using TeamPilot.Application.TicketQuestions.Dtos;
using TeamPilot.Application.Tickets.Dtos;
using TeamPilot.Domain.Entities;

namespace TeamPilot.API.Controllers;

[ApiController]
[Route("api/tickets/{ticketId:guid}/questions")]
public class TicketQuestionsController(ITicketQuestionService ticketQuestionService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TicketQuestionDto>>> ListByTicket(Guid ticketId, CancellationToken cancellationToken)
    {
        var questions = await ticketQuestionService.ListByTicketAsync(ticketId, cancellationToken);
        return Ok(questions.Select(ToDto));
    }

    [HttpPost("{questionId:guid}/answer")]
    public async Task<ActionResult<TicketDto>> Answer(Guid ticketId, Guid questionId, [FromBody] AnswerTicketQuestionRequest request, CancellationToken cancellationToken)
    {
        var ticket = await ticketQuestionService.AnswerAsync(ticketId, questionId, request, cancellationToken);
        return Ok(ticket);
    }

    private static TicketQuestionDto ToDto(TicketQuestion question) => new(
        question.Id,
        question.TicketId,
        question.AgentId,
        question.Kind,
        question.Prompt,
        question.Status,
        question.AnswerText,
        question.AnsweredBy,
        question.AnsweredAtUtc,
        question.CreatedAtUtc);
}
