using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TeamPilot.Application.Agents;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common;
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

/// <summary>
/// <see cref="CreateAsync"/> and <see cref="UpdateAsync"/> only need <see cref="IGitService"/>
/// itself, up front, to verify the requested base branch actually exists on the remote before
/// persisting anything - neither needs <see cref="IWorkflowService"/>, <see cref="IAgentService"/>,
/// an event broadcaster, or a logger, since <see cref="IBackgroundTaskRunner"/>'s contract is that
/// the detached clone delegate (only <see cref="CreateAsync"/> dispatches one) resolves every one
/// of those fresh from its own scope rather than closing over this instance's (see
/// <see cref="StartCloneDetached"/>).
/// </summary>
public sealed class ProjectService(
    IProjectRepository projectRepository,
    ITicketRepository ticketRepository,
    IUserRepository userRepository,
    ICurrentUserContext currentUser,
    IProjectAccessGuard projectAccessGuard,
    IGitService gitService,
    IGitCredentialProtector credentialProtector,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork,
    IBackgroundTaskRunner backgroundTaskRunner,
    IValidator<CreateProjectRequest> createValidator,
    IValidator<UpdateProjectRequest> updateValidator) : IProjectService
{
    private const string DefaultBaseBranch = "main";

    /// <summary>
    /// Persists the project immediately, in <see cref="ProjectStatus.Cloning"/>, then dispatches
    /// the actual clone detached (same <see cref="IBackgroundTaskRunner"/> pattern as
    /// <c>Orchestration.OrchestrationService.RunPipelineDetached</c>) instead of awaiting it
    /// inline - a large repository's clone could otherwise block this request for minutes. The
    /// project row exists (and its id is known to the caller) before the clone even starts, so
    /// the client can subscribe to <see cref="ProjectEventTypes.ProjectCloneProgress"/> on
    /// <c>GET /api/projects/{id}/events</c> right away to show a live progress bar; a failed clone
    /// leaves the row visible as <see cref="ProjectStatus.Failed"/> rather than vanishing.
    /// Before any of that, the requested base branch (or <see cref="DefaultBaseBranch"/> when none
    /// is given - mirrors <see cref="Project.Create"/>'s own default) is checked against the
    /// remote itself via <see cref="IGitService.RemoteBranchExistsAsync"/>: a nonexistent branch
    /// throws <see cref="GitOperationException"/> and no project row is created at all, rather
    /// than persisting one that will only fail to clone later.
    /// </summary>
    public async Task<ProjectDto> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.EnsureValidAsync(request, cancellationToken);

        var baseBranch = string.IsNullOrWhiteSpace(request.BaseBranch) ? DefaultBaseBranch : request.BaseBranch.Trim();
        var baseBranchExists = await gitService.RemoteBranchExistsAsync(request.RemoteUrl, request.AccessToken, baseBranch, cancellationToken);
        if (!baseBranchExists)
        {
            throw new GitOperationException($"Branch '{baseBranch}' does not exist in repository '{request.RemoteUrl}'.");
        }

        var encryptedAccessToken = credentialProtector.Protect(request.AccessToken);
        var project = Project.Create(request.Name, request.Description, request.RemoteUrl, encryptedAccessToken, baseBranch);

        await projectRepository.AddAsync(project, cancellationToken);

        await auditLogger.LogActionAsync(AuditEventType.ProjectCreated, $"Project '{project.Name}' created.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        StartCloneDetached(project.Id, project.RemoteUrl, request.AccessToken);

        // Brand new project - no tickets exist yet, so no need to query for counts.
        return ToDto(project, counts: null);
    }

    /// <summary>
    /// Runs the clone off the request thread, in its own DI scope (see
    /// <see cref="IBackgroundTaskRunner"/>), reporting progress via
    /// <see cref="ProjectEventTypes.ProjectCloneProgress"/> throughout. On success, provisions the
    /// same default workflow/Live Agent <see cref="CreateAsync"/> used to set up inline; on
    /// failure, records why on the project itself instead of losing it.
    /// </summary>
    private void StartCloneDetached(Guid projectId, string remoteUrl, string accessToken)
    {
        backgroundTaskRunner.Run(async (services, backgroundCancellationToken) =>
        {
            var backgroundProjectRepository = services.GetRequiredService<IProjectRepository>();
            var backgroundGitService = services.GetRequiredService<IGitService>();
            var backgroundWorkflowService = services.GetRequiredService<IWorkflowService>();
            var backgroundAgentService = services.GetRequiredService<IAgentService>();
            var backgroundAuditLogger = services.GetRequiredService<IAuditLogger>();
            var backgroundUnitOfWork = services.GetRequiredService<IUnitOfWork>();
            var backgroundEventBroadcaster = services.GetRequiredService<IProjectEventBroadcaster>();
            var backgroundLogger = services.GetRequiredService<ILogger<ProjectService>>();

            var progress = new Progress<GitCloneProgress>(p =>
                backgroundEventBroadcaster.Publish(
                    projectId,
                    new ProjectEvent(
                        ProjectEventTypes.ProjectCloneProgress,
                        projectId,
                        null,
                        DateTime.UtcNow,
                        new CloneProgressPayload(p.ReceivedObjects, p.TotalObjects, p.ReceivedBytes, ProjectStatus.Cloning, null))));

            try
            {
                var sandboxPath = await backgroundGitService.CloneAsync(projectId, remoteUrl, accessToken, progress, backgroundCancellationToken);

                var project = await backgroundProjectRepository.GetByIdAsync(projectId, backgroundCancellationToken)
                    ?? throw new InvalidOperationException($"Project '{projectId}' was deleted while its clone was in progress.");

                project.MarkCloned(sandboxPath);

                // Same provisioning CreateAsync used to do inline before the clone succeeded -
                // now done here, once the clone has actually landed.
                await backgroundWorkflowService.EnsureDefaultWorkflowAsync(project.Id, backgroundCancellationToken);
                await backgroundAgentService.EnsureLiveAgentAsync(project.Id, backgroundCancellationToken);

                await backgroundUnitOfWork.SaveChangesAsync(backgroundCancellationToken);

                backgroundEventBroadcaster.Publish(
                    projectId,
                    new ProjectEvent(ProjectEventTypes.ProjectCloneProgress, projectId, null, DateTime.UtcNow,
                        new CloneProgressPayload(0, 0, 0, ProjectStatus.Ready, null)));
            }
            catch (Exception ex)
            {
                backgroundLogger.LogError(ex, "Clone failed for project {ProjectId}.", projectId);

                var project = await backgroundProjectRepository.GetByIdAsync(projectId, backgroundCancellationToken);
                if (project is not null)
                {
                    project.MarkCloneFailed(ex.Message);
                    await backgroundAuditLogger.LogActionAsync(AuditEventType.ProjectCloneFailed, $"Project '{project.Name}' failed to clone: {ex.Message}", backgroundCancellationToken);
                    await backgroundUnitOfWork.SaveChangesAsync(backgroundCancellationToken);
                }

                backgroundEventBroadcaster.Publish(
                    projectId,
                    new ProjectEvent(ProjectEventTypes.ProjectCloneProgress, projectId, null, DateTime.UtcNow,
                        new CloneProgressPayload(0, 0, 0, ProjectStatus.Failed, ex.Message)));
            }
        });
    }

    public async Task<ProjectDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await projectRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), id);

        if (project.IsRemoved)
        {
            throw new NotFoundException(nameof(Project), id);
        }

        await projectAccessGuard.EnsureAccessAsync(id, cancellationToken);

        var counts = await ticketRepository.GetStatusCountsByProjectAsync([id], cancellationToken);

        return ToDto(project, counts.GetValueOrDefault(id));
    }

    public async Task<IReadOnlyList<ProjectDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var projects = (await projectRepository.ListAsync(cancellationToken))
            .Where(p => !p.IsRemoved)
            .ToList();

        if (!currentUser.IsInRole(UserRole.Admin))
        {
            var assignedProjectIds = await userRepository.GetAssignedProjectIdsAsync(currentUser.UserId, cancellationToken);
            projects = projects.Where(p => assignedProjectIds.Contains(p.Id)).ToList();
        }

        var counts = await ticketRepository.GetStatusCountsByProjectAsync(
            projects.Select(p => p.Id).ToList(), cancellationToken);

        return projects.Select(p => ToDto(p, counts.GetValueOrDefault(p.Id))).ToList();
    }

    /// <summary>
    /// Hides the project from the UI (<see cref="Project.Remove"/>) as long as none of its
    /// tickets are actively running or awaiting approval - <see cref="TicketStatus.InProgress"/>
    /// or <see cref="TicketStatus.ForReview"/>. A <see cref="TicketStatus.Blocked"/> ticket
    /// doesn't block removal (unlike <c>WorkflowService</c>'s pipeline lock): its pipeline is
    /// merely paused, and hiding the project doesn't touch the Git repository or ticket history
    /// it's paused against.
    /// </summary>
    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await projectRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), id);

        var inProgressCount = await ticketRepository.CountByStatusAsync(id, TicketStatus.InProgress, cancellationToken);
        var forReviewCount = await ticketRepository.CountByStatusAsync(id, TicketStatus.ForReview, cancellationToken);
        var activeCount = inProgressCount + forReviewCount;

        if (activeCount > 0)
        {
            throw new ProjectHasActiveTicketsException(activeCount);
        }

        project.Remove();

        await auditLogger.LogActionAsync(AuditEventType.ProjectRemoved, $"Project '{project.Name}' removed.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProjectDto> UpdateAsync(Guid id, UpdateProjectRequest request, CancellationToken cancellationToken = default)
    {
        await updateValidator.EnsureValidAsync(request, cancellationToken);

        var project = await projectRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), id);

        // Same remote check CreateAsync does up front (see there) - a blank AccessToken here
        // means "keep the currently stored token" (see UpdateProjectRequest), so that's what's
        // used to authenticate the check when a new one wasn't supplied.
        var accessTokenForCheck = string.IsNullOrWhiteSpace(request.AccessToken)
            ? credentialProtector.Unprotect(project.EncryptedAccessToken)
            : request.AccessToken;
        var baseBranchExists = await gitService.RemoteBranchExistsAsync(project.RemoteUrl, accessTokenForCheck, request.BaseBranch, cancellationToken);
        if (!baseBranchExists)
        {
            throw new GitOperationException($"Branch '{request.BaseBranch}' does not exist in repository '{project.RemoteUrl}'.");
        }

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
        project.Status,
        project.CloneFailureReason,
        project.CreatedAtUtc,
        project.UpdatedAtUtc,
        new TicketStatusCountsDto(
            ToDo: counts?.GetValueOrDefault(TicketStatus.ToDo) ?? 0,
            InProgress: counts?.GetValueOrDefault(TicketStatus.InProgress) ?? 0,
            Blocked: counts?.GetValueOrDefault(TicketStatus.Blocked) ?? 0,
            ForReview: counts?.GetValueOrDefault(TicketStatus.ForReview) ?? 0,
            Done: counts?.GetValueOrDefault(TicketStatus.Done) ?? 0));
}
