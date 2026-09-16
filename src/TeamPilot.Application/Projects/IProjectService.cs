using TeamPilot.Application.Projects.Dtos;

namespace TeamPilot.Application.Projects;

public interface IProjectService
{
    Task<ProjectDto> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken = default);

    Task<ProjectDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<ProjectDto> UpdateAsync(Guid id, UpdateProjectRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hides the project from the UI without deleting its remote repository, sandbox clone, or
    /// any history - see <see cref="Domain.Entities.Project.Remove"/>. Throws
    /// <see cref="Common.Exceptions.ProjectHasActiveTicketsException"/> if the project has any
    /// ticket <c>InProgress</c> or <c>ForReview</c>.
    /// </summary>
    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}
