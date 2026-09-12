using TeamPilot.Domain.Entities;

namespace TeamPilot.Application.Orchestration;

public interface IStageExecutionRepository
{
    Task AddAsync(StageExecution execution, CancellationToken cancellationToken = default);

    /// <summary>
    /// The most recent <see cref="StageExecution"/> per agent for this ticket, as of right now -
    /// used by <see cref="OrchestrationService"/> to hand each stage its own prior output on a
    /// review-triggered re-run, taken as one snapshot before that run adds any new rows.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, StageExecution>> GetLatestByTicketAsync(Guid ticketId, CancellationToken cancellationToken = default);
}
