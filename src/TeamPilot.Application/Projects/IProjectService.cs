using TeamPilot.Application.Projects.Dtos;

namespace TeamPilot.Application.Projects;

public interface IProjectService
{
    Task<ProjectDto> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken = default);

    Task<ProjectDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<ProjectDto> UpdateAsync(Guid id, UpdateProjectRequest request, CancellationToken cancellationToken = default);
}
