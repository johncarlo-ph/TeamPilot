using TeamPilot.Application.Pipelines.Dtos;

namespace TeamPilot.Application.Pipelines;

public interface IPipelineService
{
    /// <summary>
    /// Queues a new pipeline run for a project - called automatically on ticket approval/merge,
    /// or manually via the API. Status tracking only; no real build/test/deploy is executed.
    /// </summary>
    Task<PipelineRunDto> TriggerAsync(Guid projectId, Guid? ticketId, string triggerReason, CancellationToken cancellationToken = default);

    Task<PipelineRunDto> StartAsync(Guid pipelineRunId, CancellationToken cancellationToken = default);

    Task<PipelineRunDto> CompleteAsync(Guid pipelineRunId, CompletePipelineRunRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PipelineRunDto>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);
}
