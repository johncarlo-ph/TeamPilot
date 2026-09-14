using TeamPilot.Application.Orchestration.Dtos;
using TeamPilot.Application.Tickets.Dtos;

namespace TeamPilot.Application.Orchestration;

public interface IOrchestrationService
{
    /// <summary>
    /// Runs a ticket through its project's configured agent workflow (provisioning the default
    /// Research -&gt; Design -&gt; Coding -&gt; Testing sequence first if the project has none): assigns
    /// every stage's agent, links a branch if the ticket doesn't have one, then runs each stage
    /// in its configured order, jumping back to an earlier stage when one configured with a
    /// loop-back reports failure (bounded per stage). Always ends by moving the ticket to
    /// ForReview. Safe to call again on a ticket already In Progress (e.g. after a RequestChanges
    /// review, or to retry a failed run) - assignment and branch-linking are idempotent.
    ///
    /// Takes minutes (multiple LLM/Git calls per stage) - callers driven by an HTTP request
    /// should normally use <see cref="StartPipelineAsync"/>/<see cref="RunPipelineDetached"/>
    /// instead of awaiting this directly, so a client disconnecting (e.g. a page refresh) can't
    /// cancel an in-flight run.
    /// </summary>
    Task<TicketPipelineResultDto> RunPipelineAsync(Guid ticketId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the ticket exists and is accessible, then kicks off <see cref="RunPipelineAsync"/>
    /// detached (see <see cref="RunPipelineDetached"/>) instead of awaiting it inline - the entry
    /// point for the initial ToDo -&gt; InProgress transition. Unlike the RequestChanges/Answer/Retry
    /// callers of <see cref="RunPipelineDetached"/>, there's no cheap synchronous domain
    /// transition available here to reflect immediately: the actual ToDo -&gt; InProgress flip only
    /// happens once <see cref="RunPipelineAsync"/> itself assigns the workflow's agents. The
    /// returned <see cref="TicketDto"/> may therefore still show ToDo - poll
    /// <c>GET api/tickets/{id}</c> (or the board's own poll) to observe the eventual
    /// InProgress/Blocked/ForReview outcome.
    /// </summary>
    Task<TicketDto> StartPipelineAsync(Guid ticketId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <see cref="RunPipelineAsync"/> detached from the caller (see
    /// <c>IBackgroundTaskRunner</c>), so a caller's own cancellation (e.g. the HTTP request that
    /// triggered this) can never abort an in-flight run. Any exception that escapes
    /// <see cref="RunPipelineAsync"/> itself blocks the ticket as a backstop, mirroring its own
    /// Git/LLM failure handling for everything else. For callers that already hold a validated
    /// ticket and made their own synchronous domain transition (e.g.
    /// <c>ApprovalGateService.RequestChanges</c>, <c>TicketQuestionService.AnswerAsync</c>/
    /// <c>RetryAsync</c>) - <see cref="StartPipelineAsync"/> is the entry point when that
    /// validation hasn't happened yet.
    /// </summary>
    void RunPipelineDetached(Guid ticketId);
}
