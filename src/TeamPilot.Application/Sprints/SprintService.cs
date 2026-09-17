using FluentValidation;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Extensions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Git;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Sprints.Dtos;
using TeamPilot.Application.Tickets;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Sprints;

public sealed class SprintService(
    ISprintRepository sprintRepository,
    IProjectRepository projectRepository,
    ITicketRepository ticketRepository,
    IProjectAccessGuard projectAccessGuard,
    IGitService gitService,
    IGitCredentialProtector credentialProtector,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork,
    IValidator<CreateSprintRequest> createValidator,
    IValidator<UpdateSprintRequest> updateValidator) : ISprintService
{
    private const string DefaultBaseBranch = "main";

    /// <summary>
    /// Same remote-branch check <c>ProjectService.CreateAsync</c> used to do for a project's
    /// base branch - it's now a per-sprint concern, checked against the parent project's remote
    /// using its stored (decrypted) access token.
    /// </summary>
    public async Task<SprintDto> CreateAsync(Guid projectId, CreateSprintRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.EnsureValidAsync(request, cancellationToken);
        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);

        var project = await projectRepository.GetByIdAsync(projectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), projectId);

        var baseBranch = string.IsNullOrWhiteSpace(request.BaseBranch) ? DefaultBaseBranch : request.BaseBranch.Trim();
        var accessToken = credentialProtector.Unprotect(project.EncryptedAccessToken);
        var baseBranchExists = await gitService.RemoteBranchExistsAsync(project.RemoteUrl, accessToken, baseBranch, cancellationToken);
        if (!baseBranchExists)
        {
            throw new GitOperationException($"Branch '{baseBranch}' does not exist in repository '{project.RemoteUrl}'.");
        }

        var sprint = Sprint.Create(projectId, request.Name, baseBranch, request.SprintStartDate, request.SprintEndDate, request.SprintGoal);

        await sprintRepository.AddAsync(sprint, cancellationToken);

        await auditLogger.LogActionAsync(AuditEventType.SprintCreated, $"Sprint '{sprint.Name}' created.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Brand new sprint - no tickets exist yet, so no need to query for counts.
        return ToDto(sprint, counts: null);
    }

    public async Task<SprintDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var sprint = await sprintRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Sprint), id);

        if (sprint.IsRemoved)
        {
            throw new NotFoundException(nameof(Sprint), id);
        }

        await projectAccessGuard.EnsureAccessAsync(sprint.ProjectId, cancellationToken);

        var counts = await ticketRepository.GetStatusCountsBySprintAsync([id], cancellationToken);

        return ToDto(sprint, counts.GetValueOrDefault(id));
    }

    public async Task<IReadOnlyList<SprintDto>> ListAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);

        var sprints = (await sprintRepository.ListAsync(projectId, cancellationToken))
            .Where(s => !s.IsRemoved)
            .ToList();

        var counts = await ticketRepository.GetStatusCountsBySprintAsync(
            sprints.Select(s => s.Id).ToList(), cancellationToken);

        return sprints.Select(s => ToDto(s, counts.GetValueOrDefault(s.Id))).ToList();
    }

    public async Task<SprintDto> UpdateAsync(Guid id, UpdateSprintRequest request, CancellationToken cancellationToken = default)
    {
        await updateValidator.EnsureValidAsync(request, cancellationToken);

        var sprint = await sprintRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Sprint), id);

        await projectAccessGuard.EnsureAccessAsync(sprint.ProjectId, cancellationToken);

        var project = await projectRepository.GetByIdAsync(sprint.ProjectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), sprint.ProjectId);

        var accessToken = credentialProtector.Unprotect(project.EncryptedAccessToken);
        var baseBranchExists = await gitService.RemoteBranchExistsAsync(project.RemoteUrl, accessToken, request.BaseBranch, cancellationToken);
        if (!baseBranchExists)
        {
            throw new GitOperationException($"Branch '{request.BaseBranch}' does not exist in repository '{project.RemoteUrl}'.");
        }

        sprint.UpdateDetails(request.Name, request.BaseBranch, request.SprintStartDate, request.SprintEndDate, request.SprintGoal);

        await auditLogger.LogActionAsync(AuditEventType.SprintUpdated, $"Sprint '{sprint.Name}' updated.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var counts = await ticketRepository.GetStatusCountsBySprintAsync([id], cancellationToken);

        return ToDto(sprint, counts.GetValueOrDefault(id));
    }

    /// <summary>
    /// Hides the sprint from the UI (<see cref="Sprint.Remove"/>) as long as none of its tickets
    /// are actively running or awaiting approval - mirrors <c>ProjectService.RemoveAsync</c>,
    /// scoped to this sprint's board instead of the whole project.
    /// </summary>
    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var sprint = await sprintRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Sprint), id);

        await projectAccessGuard.EnsureAccessAsync(sprint.ProjectId, cancellationToken);

        var inProgressCount = await ticketRepository.CountByStatusBySprintAsync(id, TicketStatus.InProgress, cancellationToken);
        var forReviewCount = await ticketRepository.CountByStatusBySprintAsync(id, TicketStatus.ForReview, cancellationToken);
        var activeCount = inProgressCount + forReviewCount;

        if (activeCount > 0)
        {
            throw new SprintHasActiveTicketsException(activeCount);
        }

        sprint.Remove();

        await auditLogger.LogActionAsync(AuditEventType.SprintRemoved, $"Sprint '{sprint.Name}' removed.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static SprintDto ToDto(Sprint sprint, IReadOnlyDictionary<TicketStatus, int>? counts) => new(
        sprint.Id,
        sprint.ProjectId,
        sprint.Name,
        sprint.BaseBranch,
        sprint.SprintStartDate,
        sprint.SprintEndDate,
        sprint.SprintGoal,
        sprint.CreatedAtUtc,
        sprint.UpdatedAtUtc,
        new TicketStatusCountsDto(
            ToDo: counts?.GetValueOrDefault(TicketStatus.ToDo) ?? 0,
            InProgress: counts?.GetValueOrDefault(TicketStatus.InProgress) ?? 0,
            Blocked: counts?.GetValueOrDefault(TicketStatus.Blocked) ?? 0,
            ForReview: counts?.GetValueOrDefault(TicketStatus.ForReview) ?? 0,
            Done: counts?.GetValueOrDefault(TicketStatus.Done) ?? 0));
}
