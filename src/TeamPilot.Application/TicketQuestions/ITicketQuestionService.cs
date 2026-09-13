using TeamPilot.Application.Orchestration.Dtos;
using TeamPilot.Application.TicketQuestions.Dtos;

namespace TeamPilot.Application.TicketQuestions;

public interface ITicketQuestionService
{
    /// <summary>
    /// Answers the pending clarifying question that's blocking the ticket, unblocks it, and
    /// immediately re-invokes the pipeline (mirroring how <c>ApprovalGateService</c>'s
    /// RequestChanges branch already re-runs the pipeline right after unblocking the ticket).
    /// </summary>
    Task<TicketPipelineResultDto> AnswerAsync(Guid ticketId, Guid questionId, AnswerTicketQuestionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retries a ticket blocked by a known operational failure (Git/LLM) - there's nothing to
    /// "answer", so this just unblocks the ticket and re-invokes the pipeline.
    /// </summary>
    Task<TicketPipelineResultDto> RetryAsync(Guid ticketId, CancellationToken cancellationToken = default);
}
