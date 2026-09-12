using TeamPilot.Application.Orchestration.Dtos;

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
    /// </summary>
    Task<TicketPipelineResultDto> RunPipelineAsync(Guid ticketId, CancellationToken cancellationToken = default);
}
