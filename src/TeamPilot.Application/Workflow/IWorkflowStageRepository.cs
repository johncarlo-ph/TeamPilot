using TeamPilot.Domain.Entities;

namespace TeamPilot.Application.Workflow;

public interface IWorkflowStageRepository
{
    /// <summary>
    /// Loads a single stage tracked for updates. Like <see cref="Commit"/>/<see cref="Review"/>/
    /// <see cref="Conflict"/> on <see cref="Ticket"/>, a <see cref="WorkflowStage"/> references
    /// its <see cref="Agent"/> by id only (no navigation property) - callers that need the
    /// agent itself load it separately via <c>IAgentRepository</c>.
    /// </summary>
    Task<WorkflowStage?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The project's full stage sequence, ordered by <see cref="WorkflowStage.Order"/> and
    /// tracked for updates (matching how <c>IAgentRepository.GetByProjectAndRoleAsync</c> is
    /// already tracked even for OrchestrationService's read-only use) - <c>WorkflowService</c>
    /// relies on this to renumber/mutate several stages from one call during a reorder or
    /// removal, and <c>OrchestrationService</c> uses it to run a ticket's pipeline.
    /// </summary>
    Task<IReadOnlyList<WorkflowStage>> ListOrderedAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task AddAsync(WorkflowStage stage, CancellationToken cancellationToken = default);

    void Remove(WorkflowStage stage);
}
