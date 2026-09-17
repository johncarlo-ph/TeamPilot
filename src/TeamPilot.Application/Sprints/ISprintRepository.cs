using TeamPilot.Domain.Entities;

namespace TeamPilot.Application.Sprints;

public interface ISprintRepository
{
    Task<Sprint?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Sprint>> ListAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task AddAsync(Sprint sprint, CancellationToken cancellationToken = default);
}
