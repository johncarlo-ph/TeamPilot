using FluentValidation;
using TeamPilot.Application.Agents;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Extensions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Git;
using TeamPilot.Application.Projects.Dtos;
using TeamPilot.Application.Tickets;
using TeamPilot.Application.Users;
using TeamPilot.Application.Workflow;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Projects;

public sealed class ProjectService(
    IProjectRepository projectRepository,
    ITicketRepository ticketRepository,
    IUserRepository userRepository,
    IAgentService agentService,
    IWorkflowService workflowService,
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

        // Every new project starts with the default Research -> Design -> Coding -> Testing
        // workflow (Testing looping back to Coding, bounded at 3 attempts) - an admin can later
        // add/remove/reorder stages and loop-backs via WorkflowService.
        await workflowService.EnsureDefaultWorkflowAsync(project.Id, cancellationToken);

        // Every project also gets one standing Live Agent - unlike the 4 pipeline agents above,
        // it isn't invoked automatically; a human chats with it directly.
        await agentService.EnsureLiveAgentAsync(project.Id, cancellationToken);

        await auditLogger.LogActionAsync(AuditEventType.ProjectCreated, $"Project '{project.Name}' created.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Brand new project - no tickets exist yet, so no need to query for counts.
        return ToDto(project, counts: null);
    }

    public async Task<ProjectDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await projectRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), id);

        await projectAccessGuard.EnsureAccessAsync(id, cancellationToken);

        var counts = await ticketRepository.GetStatusCountsByProjectAsync([id], cancellationToken);

        return ToDto(project, counts.GetValueOrDefault(id));
    }

    public async Task<IReadOnlyList<ProjectDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var projects = await projectRepository.ListAsync(cancellationToken);

        if (!currentUser.IsInRole(UserRole.Admin))
        {
            var assignedProjectIds = await userRepository.GetAssignedProjectIdsAsync(currentUser.UserId, cancellationToken);
            projects = projects.Where(p => assignedProjectIds.Contains(p.Id)).ToList();
        }

        var counts = await ticketRepository.GetStatusCountsByProjectAsync(
            projects.Select(p => p.Id).ToList(), cancellationToken);

        return projects.Select(p => ToDto(p, counts.GetValueOrDefault(p.Id))).ToList();
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

        var counts = await ticketRepository.GetStatusCountsByProjectAsync([id], cancellationToken);

        return ToDto(project, counts.GetValueOrDefault(id));
    }

    private static ProjectDto ToDto(Project project, IReadOnlyDictionary<TicketStatus, int>? counts) => new(
        project.Id,
        project.Name,
        project.Description,
        project.RemoteUrl,
        project.BaseBranch,
        project.CreatedAtUtc,
        project.UpdatedAtUtc,
        new TicketStatusCountsDto(
            ToDo: counts?.GetValueOrDefault(TicketStatus.ToDo) ?? 0,
            InProgress: counts?.GetValueOrDefault(TicketStatus.InProgress) ?? 0,
            Blocked: counts?.GetValueOrDefault(TicketStatus.Blocked) ?? 0,
            ForReview: counts?.GetValueOrDefault(TicketStatus.ForReview) ?? 0,
            Done: counts?.GetValueOrDefault(TicketStatus.Done) ?? 0));
}
