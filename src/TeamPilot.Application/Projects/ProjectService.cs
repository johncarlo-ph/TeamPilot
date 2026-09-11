using FluentValidation;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Extensions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Projects.Dtos;
using TeamPilot.Application.Users;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Projects;

public sealed class ProjectService(
    IProjectRepository projectRepository,
    IUserRepository userRepository,
    ICurrentUserContext currentUser,
    IProjectAccessGuard projectAccessGuard,
    IUnitOfWork unitOfWork,
    IValidator<CreateProjectRequest> createValidator,
    IValidator<UpdateProjectRequest> updateValidator) : IProjectService
{
    public async Task<ProjectDto> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.EnsureValidAsync(request, cancellationToken);

        var project = Project.Create(request.Name, request.Description, request.RepositoryPath);
        await projectRepository.AddAsync(project, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(project);
    }

    public async Task<ProjectDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await projectRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), id);

        await projectAccessGuard.EnsureAccessAsync(id, cancellationToken);

        return ToDto(project);
    }

    public async Task<IReadOnlyList<ProjectDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var projects = await projectRepository.ListAsync(cancellationToken);

        if (currentUser.IsInRole(UserRole.Admin))
        {
            return projects.Select(ToDto).ToList();
        }

        var assignedProjectIds = await userRepository.GetAssignedProjectIdsAsync(currentUser.UserId, cancellationToken);

        return projects
            .Where(p => assignedProjectIds.Contains(p.Id))
            .Select(ToDto)
            .ToList();
    }

    public async Task<ProjectDto> UpdateAsync(Guid id, UpdateProjectRequest request, CancellationToken cancellationToken = default)
    {
        await updateValidator.EnsureValidAsync(request, cancellationToken);

        var project = await projectRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), id);

        project.UpdateDetails(request.Name, request.Description, request.RepositoryPath);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(project);
    }

    private static ProjectDto ToDto(Project project) => new(
        project.Id,
        project.Name,
        project.Description,
        project.RepositoryPath,
        project.CreatedAtUtc,
        project.UpdatedAtUtc);
}
