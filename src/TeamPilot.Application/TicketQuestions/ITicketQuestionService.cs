using TeamPilot.Application.TicketQuestions.Dtos;
using TeamPilot.Application.Tickets.Dtos;

namespace TeamPilot.Application.TicketQuestions;

public interface ITicketQuestionService
{
    /// <summary>
    /// Answers the pending clarifying question that's blocking the ticket, unblocks it, and
    /// re-invokes the pipeline detached (mirroring how <c>ApprovalGateService</c>'s
    /// RequestChanges branch already re-runs the pipeline right after unblocking the ticket) -
    /// see <c>IOrchestrationService.RunPipelineDetached</c>. Poll <c>GET api/tickets/{id}</c> (or
    /// the board's own poll) to observe the pipeline's eventual outcome.
    /// </summary>
    Task<TicketDto> AnswerAsync(Guid ticketId, Guid questionId, AnswerTicketQuestionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retries a ticket blocked by a known operational failure (Git/LLM) - there's nothing to
    /// "answer", so this just unblocks the ticket and re-invokes the pipeline detached (see
    /// <c>IOrchestrationService.RunPipelineDetached</c>).
    /// </summary>
    Task<TicketDto> RetryAsync(Guid ticketId, CancellationToken cancellationToken = default);
}
