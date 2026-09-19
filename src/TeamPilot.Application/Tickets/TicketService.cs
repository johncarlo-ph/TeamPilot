using FluentValidation;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Extensions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Git;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Sprints;
using TeamPilot.Application.TicketPipelineNotes;
using TeamPilot.Application.TicketProjectInstructions;
using TeamPilot.Application.Tickets.Dtos;
using TeamPilot.Application.Tickets.Mentions;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Tickets;

public sealed class TicketService(
    ITicketRepository ticketRepository,
    IProjectRepository projectRepository,
    ISprintRepository sprintRepository,
    IGitService gitService,
    IGitCredentialProtector credentialProtector,
    IProjectAccessGuard projectAccessGuard,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork,
    IPipelineRunTracker pipelineRunTracker,
    IProjectEventBroadcaster eventBroadcaster,
    ITicketProjectInstructionRepository ticketProjectInstructionRepository,
    ITicketPipelineNoteRepository ticketPipelineNoteRepository,
    IValidator<CreateTicketRequest> createValidator,
    IValidator<CreateBranchRequest> createBranchValidator,
    IValidator<CancelTicketRequest> cancelValidator,
    IValidator<AssignTicketToSprintRequest> assignToSprintValidator) : ITicketService
{
    private void PublishTicketChanged(Ticket ticket) =>
        eventBroadcaster.Publish(ticket.ProjectId, new ProjectEvent(ProjectEventTypes.TicketChanged, ticket.ProjectId, ticket.Id, DateTime.UtcNow));

    /// <summary>
    /// Every "@"-mention parsed out of a submitted description (see <see cref="MentionParser"/>) -
    /// Project, Ticket, or File category - must point at something that actually exists and that
    /// the creating user can actually reach - otherwise the Research/Design stages would later be
    /// asked to ground themselves in a reference nobody can resolve, or a project the user has no
    /// business pointing agents at. A File mention's project (see <see cref="Mention.Id"/> for
    /// <see cref="MentionType.File"/>) is checked the exact same way a direct Project mention is,
    /// PLUS two File-specific rules: a cross-project file (one whose project isn't
    /// <paramref name="ownProjectId"/>) is rejected (<see cref="FileMentionProjectNotReferencedException"/>)
    /// unless that same project is also directly Project-mentioned somewhere in the description -
    /// matching what actually grants Research/Design read access to it (see
    /// <c>Orchestration.OrchestrationService.ResolveReferencedProjectsAsync</c>) - and the file
    /// path itself IS checked against that project's live repository (unlike a ticket/project
    /// mention, which points at a stable database row, a path can trivially be stale or mistyped),
    /// which requires the project to be <see cref="ProjectStatus.Ready"/>. Reuses existing
    /// exception types (<see cref="NotFoundException"/>, and <see cref="IProjectAccessGuard"/>'s
    /// <see cref="ForbiddenException"/>) where it can, rather than inventing new ones.
    /// </summary>
    private async Task ValidateMentionsAsync(string? description, Guid ownProjectId, CancellationToken cancellationToken)
    {
        var mentions = MentionParser.Parse(description);
        if (mentions.Count == 0)
        {
            return;
        }

        var ticketIds = mentions.Where(m => m.Type == MentionType.Ticket).Select(m => m.Id).Distinct().ToList();
        var projectIds = mentions.Where(m => m.Type is MentionType.Project or MentionType.File).Select(m => m.Id).Distinct().ToList();

        var foundTickets = ticketIds.Count > 0
            ? await ticketRepository.GetByIdsAsync(ticketIds, cancellationToken)
            : [];

        var missingTicketIds = ticketIds.Except(foundTickets.Select(t => t.Id)).ToList();
        if (missingTicketIds.Count > 0)
        {
            throw new NotFoundException(nameof(Ticket), missingTicketIds[0]);
        }

        var foundProjects = projectIds.Count > 0
            ? await projectRepository.GetByIdsAsync(projectIds, cancellationToken)
            : [];
        var foundProjectsById = foundProjects.ToDictionary(p => p.Id);

        var missingProjectIds = projectIds.Except(foundProjectsById.Keys).ToList();
        if (missingProjectIds.Count > 0)
        {
            throw new NotFoundException(nameof(Project), missingProjectIds[0]);
        }

        var accessibleProjectIds = foundTickets.Select(t => t.ProjectId)
            .Concat(foundProjects.Select(p => p.Id))
            .Distinct();

        foreach (var projectId in accessibleProjectIds)
        {
            await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);
        }

        var directlyMentionedProjectIds = mentions.Where(m => m.Type == MentionType.Project).Select(m => m.Id).ToHashSet();

        foreach (var fileMention in mentions.Where(m => m.Type == MentionType.File))
        {
            var fileProject = foundProjectsById[fileMention.Id];

            if (fileMention.Id != ownProjectId && !directlyMentionedProjectIds.Contains(fileMention.Id))
            {
                throw new FileMentionProjectNotReferencedException(fileProject.Name, fileMention.FilePath!);
            }

            if (fileProject.Status != ProjectStatus.Ready)
            {
                throw new ProjectNotReadyException(fileProject.Name);
            }

            if (!await gitService.FileExistsAsync(fileProject.RepositoryPath, fileMention.FilePath!, branchName: null, cancellationToken))
            {
                throw new NotFoundException("File", fileMention.FilePath!);
            }
        }
    }

    public async Task<TicketDto> CreateAsync(Guid sprintId, CreateTicketRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.EnsureValidAsync(request, cancellationToken);

        var sprint = await sprintRepository.GetByIdAsync(sprintId, cancellationToken)
            ?? throw new NotFoundException(nameof(Sprint), sprintId);

        await ValidateMentionsAsync(request.Description, sprint.ProjectId, cancellationToken);
        await projectAccessGuard.EnsureAccessAsync(sprint.ProjectId, cancellationToken);

        var project = await projectRepository.GetByIdAsync(sprint.ProjectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), sprint.ProjectId);

        if (project.Status != ProjectStatus.Ready)
        {
            throw new ProjectNotReadyException(project.Name);
        }

        var ticket = Ticket.Create(sprint.ProjectId, sprintId, request.Title, request.Description, request.AcceptanceCriteria);
        await ticketRepository.AddAsync(ticket, cancellationToken);

        await auditLogger.LogActionAsync(AuditEventType.TicketCreated, $"Ticket '{ticket.Title}' created.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        PublishTicketChanged(ticket);

        return TicketMappings.ToDto(ticket, pipelineRunTracker.IsRunning(ticket.Id));
    }

    /// <summary>
    /// Creates a ticket in the project's backlog - not yet assigned to any sprint (see
    /// <see cref="AssignToSprintAsync"/>). Otherwise identical to <see cref="CreateAsync"/>,
    /// including requiring <see cref="ProjectStatus.Ready"/>: that keeps "every ticket that
    /// exists was created against a Ready project" true without needing extra readiness checks
    /// later, at pipeline-start/branch-link time.
    /// </summary>
    public async Task<TicketDto> CreateBacklogAsync(Guid projectId, CreateTicketRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.EnsureValidAsync(request, cancellationToken);
        await ValidateMentionsAsync(request.Description, projectId, cancellationToken);
        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);

        var project = await projectRepository.GetByIdAsync(projectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), projectId);

        if (project.Status != ProjectStatus.Ready)
        {
            throw new ProjectNotReadyException(project.Name);
        }

        var ticket = Ticket.Create(projectId, null, request.Title, request.Description, request.AcceptanceCriteria);
        await ticketRepository.AddAsync(ticket, cancellationToken);

        await auditLogger.LogActionAsync(AuditEventType.TicketCreated, $"Ticket '{ticket.Title}' created in backlog.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        PublishTicketChanged(ticket);

        return TicketMappings.ToDto(ticket, pipelineRunTracker.IsRunning(ticket.Id));
    }

    public async Task<TicketDetailDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ticket = await ticketRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), id);

        await projectAccessGuard.EnsureAccessAsync(ticket.ProjectId, cancellationToken);

        var instructions = await ticketProjectInstructionRepository.ListByTicketAsync(id, cancellationToken);
        var pipelineNotes = await ticketPipelineNoteRepository.ListByTicketAsync(id, cancellationToken);

        return TicketMappings.ToDetailDto(ticket, instructions, pipelineNotes, pipelineRunTracker.IsRunning(ticket.Id));
    }

    public async Task<IReadOnlyList<TicketDto>> ListAsync(Guid sprintId, TicketStatus? status, CancellationToken cancellationToken = default)
    {
        var sprint = await sprintRepository.GetByIdAsync(sprintId, cancellationToken)
            ?? throw new NotFoundException(nameof(Sprint), sprintId);

        await projectAccessGuard.EnsureAccessAsync(sprint.ProjectId, cancellationToken);

        var tickets = await ticketRepository.ListAsync(sprintId, status, cancellationToken);
        return tickets.Select(t => TicketMappings.ToDto(t, pipelineRunTracker.IsRunning(t.Id))).ToList();
    }

    public async Task<IReadOnlyList<TicketDto>> ListByProjectAsync(Guid projectId, TicketStatus? status, CancellationToken cancellationToken = default)
    {
        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);

        var tickets = await ticketRepository.ListByProjectAsync(projectId, status, cancellationToken);
        return tickets.Select(t => TicketMappings.ToDto(t, pipelineRunTracker.IsRunning(t.Id))).ToList();
    }

    public async Task<IReadOnlyList<TicketDto>> ListBacklogAsync(Guid projectId, TicketStatus? status, CancellationToken cancellationToken = default)
    {
        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);

        var tickets = await ticketRepository.ListBacklogAsync(projectId, status, cancellationToken);
        return tickets.Select(t => TicketMappings.ToDto(t, pipelineRunTracker.IsRunning(t.Id))).ToList();
    }

    public async Task<TicketDto> AssignToSprintAsync(Guid ticketId, AssignTicketToSprintRequest request, CancellationToken cancellationToken = default)
    {
        await assignToSprintValidator.EnsureValidAsync(request, cancellationToken);

        var ticket = await ticketRepository.GetByIdAsync(ticketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), ticketId);

        await projectAccessGuard.EnsureAccessAsync(ticket.ProjectId, cancellationToken);

        var sprint = await sprintRepository.GetByIdAsync(request.SprintId, cancellationToken)
            ?? throw new NotFoundException(nameof(Sprint), request.SprintId);

        if (sprint.ProjectId != ticket.ProjectId)
        {
            throw new NotFoundException(nameof(Sprint), request.SprintId);
        }

        ticket.AssignToSprint(request.SprintId);

        await auditLogger.LogActionAsync(AuditEventType.TicketAssignedToSprint, $"Ticket '{ticket.Title}' assigned to sprint '{sprint.Name}'.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        PublishTicketChanged(ticket);

        return TicketMappings.ToDto(ticket, pipelineRunTracker.IsRunning(ticket.Id));
    }

    public async Task<TicketDto> MoveToBacklogAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        var ticket = await ticketRepository.GetByIdAsync(ticketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), ticketId);

        await projectAccessGuard.EnsureAccessAsync(ticket.ProjectId, cancellationToken);

        ticket.MoveToBacklog();

        await auditLogger.LogActionAsync(AuditEventType.TicketMovedToBacklog, $"Ticket '{ticket.Title}' moved to the backlog.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        PublishTicketChanged(ticket);

        return TicketMappings.ToDto(ticket, pipelineRunTracker.IsRunning(ticket.Id));
    }

    public async Task<TicketDto> MoveToReviewAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ticket = await ticketRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), id);

        await projectAccessGuard.EnsureAccessAsync(ticket.ProjectId, cancellationToken);

        ticket.MoveToReview();

        await auditLogger.LogActionAsync(AuditEventType.TicketMovedToReview, $"Ticket '{ticket.Title}' moved to review.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        PublishTicketChanged(ticket);

        return TicketMappings.ToDto(ticket, pipelineRunTracker.IsRunning(ticket.Id));
    }

    public async Task<TicketDto> CancelAsync(Guid id, CancelTicketRequest request, CancellationToken cancellationToken = default)
    {
        await cancelValidator.EnsureValidAsync(request, cancellationToken);

        var ticket = await ticketRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), id);

        await projectAccessGuard.EnsureAccessAsync(ticket.ProjectId, cancellationToken);

        var project = await projectRepository.GetByIdAsync(ticket.ProjectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), ticket.ProjectId);

        ticket.Cancel(request.Reason);
        await auditLogger.LogActionAsync(AuditEventType.TicketCancelled, $"Ticket '{ticket.Title}' cancelled.", cancellationToken);

        // Held for the rest of this method, including the SaveChangesAsync below - the same lock
        // OrchestrationService.LinkBranchAsync/RunCodingStageAsync acquire before creating or
        // pushing to a ticket's branch, re-checking its status fresh once they have it.
        // Persisting Cancelled here before releasing the lock guarantees neither of those can
        // complete afterward without seeing it, so an in-flight pipeline run can never resurrect
        // the branch this cancellation is about to delete (or, if it has no branch yet, link a
        // new one that would then never get deleted) - see docs/application.md. A cancelled
        // ticket is also terminal and drops off the board with no way back to its detail page,
        // so its branch's "Delete Branch" button would be unreachable if we waited for a manual
        // click - clean it up as part of cancelling instead.
        await using (await GitRepositoryLock.AcquireAsync(project.RepositoryPath, cancellationToken))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(ticket.BranchName))
            {
                await DeleteLinkedBranchAsync(ticket, project, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }

        PublishTicketChanged(ticket);

        return TicketMappings.ToDto(ticket, pipelineRunTracker.IsRunning(ticket.Id));
    }

    public async Task<TicketDto> DeleteBranchAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ticket = await ticketRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), id);

        await projectAccessGuard.EnsureAccessAsync(ticket.ProjectId, cancellationToken);

        if (string.IsNullOrWhiteSpace(ticket.BranchName))
        {
            throw new InvalidOperationException("Ticket has no linked branch to delete.");
        }

        var project = await projectRepository.GetByIdAsync(ticket.ProjectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), ticket.ProjectId);

        // Retained as a manual fallback for a ticket that was cancelled before automatic
        // deletion existed, or whose automatic deletion needs retrying.
        await using (await GitRepositoryLock.AcquireAsync(project.RepositoryPath, cancellationToken))
        {
            await DeleteLinkedBranchAsync(ticket, project, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        PublishTicketChanged(ticket);

        return TicketMappings.ToDto(ticket, pipelineRunTracker.IsRunning(ticket.Id));
    }

    // Validate before touching Git - UnlinkBranch guards that this is only allowed once the
    // ticket is Cancelled, and there's no point deleting the remote branch if that check is
    // about to fail anyway. Callers hold the GitRepositoryLock for project.RepositoryPath already.
    private async Task DeleteLinkedBranchAsync(Ticket ticket, Project project, CancellationToken cancellationToken)
    {
        var branchName = ticket.BranchName!;
        ticket.UnlinkBranch();

        // A ticket can only have a branch once it's been assigned to a sprint (LinkBranchAsync
        // below guards that), so SprintId is guaranteed non-null here.
        var sprint = await sprintRepository.GetByIdAsync(ticket.SprintId!.Value, cancellationToken)
            ?? throw new NotFoundException(nameof(Sprint), ticket.SprintId.Value);

        var accessToken = credentialProtector.Unprotect(project.EncryptedAccessToken);
        await gitService.DeleteBranchAsync(project.RepositoryPath, branchName, sprint.BaseBranch, accessToken, cancellationToken);

        await auditLogger.LogActionAsync(AuditEventType.GitBranchDeleted, $"Branch '{branchName}' deleted for ticket '{ticket.Title}'.", cancellationToken);
    }

    public async Task<TicketDto> LinkBranchAsync(Guid ticketId, string branchName, CancellationToken cancellationToken = default)
    {
        await createBranchValidator.EnsureValidAsync(new CreateBranchRequest(ticketId, branchName), cancellationToken);

        var ticket = await ticketRepository.GetByIdAsync(ticketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), ticketId);

        await projectAccessGuard.EnsureAccessAsync(ticket.ProjectId, cancellationToken);

        if (ticket.SprintId is null)
        {
            throw new TicketNotAssignedToSprintException(ticket.Title);
        }

        var project = await projectRepository.GetByIdAsync(ticket.ProjectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), ticket.ProjectId);

        var sprint = await sprintRepository.GetByIdAsync(ticket.SprintId.Value, cancellationToken)
            ?? throw new NotFoundException(nameof(Sprint), ticket.SprintId.Value);

        var otherTicketWithBranch = await ticketRepository.GetByBranchNameAsync(ticket.ProjectId, branchName, cancellationToken);
        if (otherTicketWithBranch is not null && otherTicketWithBranch.Id != ticket.Id)
        {
            throw new BranchAlreadyLinkedException(branchName, otherTicketWithBranch.Title);
        }

        var accessToken = credentialProtector.Unprotect(project.EncryptedAccessToken);

        await using (await GitRepositoryLock.AcquireAsync(project.RepositoryPath, cancellationToken))
        {
            // Fetch first so the new branch is cut from the remote's current tip of the base
            // branch, not a possibly-stale local one.
            await gitService.FetchAsync(project.RepositoryPath, accessToken, cancellationToken);
            await gitService.EnsureBranchAsync(project.RepositoryPath, branchName, sprint.BaseBranch, cancellationToken);
            await gitService.PushAsync(project.RepositoryPath, branchName, accessToken, cancellationToken);
        }

        ticket.LinkBranch(branchName);

        await auditLogger.LogActionAsync(AuditEventType.GitBranchCreated, $"Branch '{branchName}' created for ticket '{ticket.Title}'.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        PublishTicketChanged(ticket);

        return TicketMappings.ToDto(ticket, pipelineRunTracker.IsRunning(ticket.Id));
    }

    public async Task<bool> BranchExistsAsync(Guid ticketId, string branchName, CancellationToken cancellationToken = default)
    {
        await createBranchValidator.EnsureValidAsync(new CreateBranchRequest(ticketId, branchName), cancellationToken);

        var ticket = await ticketRepository.GetByIdAsync(ticketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), ticketId);

        await projectAccessGuard.EnsureAccessAsync(ticket.ProjectId, cancellationToken);

        var project = await projectRepository.GetByIdAsync(ticket.ProjectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), ticket.ProjectId);

        var accessToken = credentialProtector.Unprotect(project.EncryptedAccessToken);

        await using (await GitRepositoryLock.AcquireAsync(project.RepositoryPath, cancellationToken))
        {
            // Fetch first so the check reflects the remote's current refs, not a possibly-stale
            // local view.
            await gitService.FetchAsync(project.RepositoryPath, accessToken, cancellationToken);

            return await gitService.BranchExistsAsync(project.RepositoryPath, branchName, cancellationToken);
        }
    }
}
