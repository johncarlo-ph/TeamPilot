using TeamPilot.Application.Orchestration.Dtos;

namespace TeamPilot.Application.Orchestration;

public interface IOrchestrationService
{
    /// <summary>
    /// Runs the standardized Research -&gt; Design -&gt; Coding -&gt; Testing pipeline for a ticket:
    /// assigns the project's four pipeline agents (provisioning them first if missing), links a
    /// branch if the ticket doesn't have one, runs each stage in order, and retries Coding with
    /// Testing's feedback (bounded) when Testing reports a failure. Always ends by moving the
    /// ticket to ForReview. Safe to call again on a ticket already In Progress (e.g. after a
    /// RequestChanges review, or to retry a failed run) - assignment and branch-linking are
    /// idempotent.
    /// </summary>
    Task<TicketPipelineResultDto> RunPipelineAsync(Guid ticketId, CancellationToken cancellationToken = default);
}
