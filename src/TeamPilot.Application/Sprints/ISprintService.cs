using TeamPilot.Application.Sprints.Dtos;

namespace TeamPilot.Application.Sprints;

public interface ISprintService
{
    Task<SprintDto> CreateAsync(Guid projectId, CreateSprintRequest request, CancellationToken cancellationToken = default);

    Task<SprintDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SprintDto>> ListAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<SprintDto> UpdateAsync(Guid id, UpdateSprintRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hides the sprint from the UI without deleting its tickets or any history - see
    /// <see cref="Domain.Entities.Sprint.Remove"/>. Throws
    /// <see cref="Common.Exceptions.SprintHasActiveTicketsException"/> if the sprint has any
    /// ticket <c>InProgress</c> or <c>ForReview</c>.
    /// </summary>
    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}
