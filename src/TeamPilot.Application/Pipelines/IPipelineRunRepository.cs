using TeamPilot.Domain.Entities;

namespace TeamPilot.Application.Pipelines;

public interface IPipelineRunRepository
{
    Task<PipelineRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PipelineRun>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task AddAsync(PipelineRun pipelineRun, CancellationToken cancellationToken = default);
}
