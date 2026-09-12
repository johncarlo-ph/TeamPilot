using FluentValidation;
using TeamPilot.Application.Agents;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Extensions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Git;
using TeamPilot.Application.Projects.Dtos;
using TeamPilot.Application.Users;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Projects;

public sealed class ProjectService(
    IProjectRepository projectRepository,
    IUserRepository userRepository,
    IAgentService agentService,
    ICurrentUserContext currentUser,
    IProjectAccessGuard projectAccessGuard,
    IGitService gitService,
    IGitCredentialProtector credentialProtector,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork,
    IValidator<CreateProjectRequest> createValidator,
    IValidator<UpdateProjectRequest> updateValidator) : IProjectService
{
    public async Task<ProjectDto> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.EnsureValidAsync(request, cancellationToken);

        var encryptedAccessToken = credentialProtector.Protect(request.AccessToken);
        var project = Project.Create(request.Name, request.Description, request.RemoteUrl, encryptedAccessToken, request.BaseBranch);

        // Clone before persisting: if the remote can't be reached with the given token, nothing
        // is saved - no partial/orphaned project row to clean up or retry.
        var sandboxPath = await gitService.CloneAsync(project.Id, project.RemoteUrl, request.AccessToken, cancellationToken);
        project.AssignSandboxPath(sandboxPath);

        await projectRepository.AddAsync(project, cancellationToken);

        // Every project always has exactly one Research/Design/Coding/Testing agent, so a
        // ticket's pipeline can unambiguously find each stage's agent without a manual
        // assignment step.
        await agentService.EnsureDefaultAgentsAsync(project.Id, cancellationToken);

        // Every project also gets one standing Live Agent - unlike the 4 pipeline agents above,
        // it isn't invoked automatically; a human chats with it directly.
        await agentService.EnsureLiveAgentAsync(project.Id, cancellationToken);

        await auditLogger.LogActionAsync(AuditEventType.ProjectCreated, $"Project '{project.Name}' created.", cancellationToken);
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

        project.UpdateDetails(request.Name, request.Description, request.BaseBranch);

        if (!string.IsNullOrWhiteSpace(request.AccessToken))
        {
            project.RotateAccessToken(credentialProtector.Protect(request.AccessToken));
        }

        await auditLogger.LogActionAsync(AuditEventType.ProjectUpdated, $"Project '{project.Name}' updated.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(project);
    }

    private static ProjectDto ToDto(Project project) => new(
        project.Id,
        project.Name,
        project.Description,
        project.RemoteUrl,
        project.BaseBranch,
        project.CreatedAtUtc,
        project.UpdatedAtUtc);
}
