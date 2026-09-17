using FluentValidation;
using Microsoft.Extensions.Logging;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Extensions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Git;
using TeamPilot.Application.Orchestration;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Reviews;
using TeamPilot.Application.Reviews.Dtos;
using TeamPilot.Application.Sprints;
using TeamPilot.Application.Tickets;
using TeamPilot.Application.Tickets.Dtos;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Approval;

public sealed class ApprovalGateService(
    ITicketRepository ticketRepository,
    IProjectRepository projectRepository,
    ISprintRepository sprintRepository,
    IReviewRepository reviewRepository,
    IGitService gitService,
    IGitCredentialProtector credentialProtector,
    IProjectAccessGuard projectAccessGuard,
    ICurrentUserContext currentUser,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork,
    IValidator<SubmitReviewRequest> validator,
    IOrchestrationService orchestrationService,
    IPipelineRunTracker pipelineRunTracker,
    IProjectEventBroadcaster eventBroadcaster,
    ILogger<ApprovalGateService> logger) : IApprovalGateService
{
    public async Task<IReadOnlyList<Review>> ListByTicketAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        var ticket = await ticketRepository.GetByIdAsync(ticketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), ticketId);

        await projectAccessGuard.EnsureAccessAsync(ticket.ProjectId, cancellationToken);

        return await reviewRepository.ListByTicketAsync(ticket.Id, cancellationToken);
    }

    public async Task<TicketDto> SubmitReviewAsync(Guid ticketId, SubmitReviewRequest request, CancellationToken cancellationToken = default)
    {
        await validator.EnsureValidAsync(request, cancellationToken);

        var ticket = await ticketRepository.GetByIdAsync(ticketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), ticketId);

        await projectAccessGuard.EnsureAccessAsync(ticket.ProjectId, cancellationToken);

        if (request.Decision is ReviewDecision.Approve or ReviewDecision.Reject
            && !currentUser.IsInRole(UserRole.Admin)
            && !currentUser.IsInRole(UserRole.Developer))
        {
            throw new ForbiddenException("Only Developers or Admins may approve or reject tickets.");
        }

        var review = Review.Create(ticket.Id, request.ReviewerName, request.Decision, request.Comments);
        ticket.RecordReview(review);

        await auditLogger.LogActionAsync(AuditEventType.ReviewSubmitted, $"Review ({request.Decision}) submitted for ticket '{ticket.Title}' by {request.ReviewerName}.", cancellationToken);

        switch (request.Decision)
        {
            case ReviewDecision.Approve:
                await ApproveAsync(ticket, cancellationToken);
                break;

            case ReviewDecision.RequestChanges:
                ticket.RequestChanges();
                await unitOfWork.SaveChangesAsync(cancellationToken);

                // Re-run the workflow rather than leaving the ticket sitting In Progress until
                // someone manually clicks Run Pipeline - RunPipelineAsync is idempotent/
                // re-entrant by design (see IOrchestrationService), so calling it again here is
                // safe. Kicked off detached (see IOrchestrationService.RunPipelineDetached)
                // instead of awaited: a re-run can take minutes (multiple LLM/Git calls per
                // stage), and the status change above already gives the caller everything it
                // needs to show the ticket as In Progress immediately.
                orchestrationService.RunPipelineDetached(ticket.ProjectId, ticket.Id);
                break;

            case ReviewDecision.Reject:
                await RejectAsync(ticket, request.Comments, cancellationToken);
                break;

            case ReviewDecision.ResolveConflict:
                // The review is recorded here; actual conflict detection/resolution is
                // performed via IConflictResolutionService against the ticket's conflicts.
                await unitOfWork.SaveChangesAsync(cancellationToken);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.Decision, "Unsupported review decision.");
        }

        eventBroadcaster.Publish(ticket.ProjectId, new ProjectEvent(ProjectEventTypes.TicketChanged, ticket.ProjectId, ticket.Id, DateTime.UtcNow));

        return TicketMappings.ToDto(ticket, pipelineRunTracker.IsRunning(ticket.Id));
    }

    /// <summary>
    /// Cancels the ticket and, if it has a linked branch, deletes that branch from the remote -
    /// unlike a plain <see cref="TicketService.CancelAsync"/> (which leaves the branch orphaned
    /// for a separate, explicit <c>DeleteBranchAsync</c> call), Reject does both atomically as
    /// one decision, since a rejected-in-review ticket's branch was never merged anywhere and so
    /// has nothing left to protect.
    /// </summary>
    private async Task RejectAsync(Ticket ticket, string? reason, CancellationToken cancellationToken)
    {
        ticket.Cancel(reason);

        if (!string.IsNullOrWhiteSpace(ticket.BranchName))
        {
            var project = await projectRepository.GetByIdAsync(ticket.ProjectId, cancellationToken)
                ?? throw new NotFoundException(nameof(Project), ticket.ProjectId);

            var sprint = await sprintRepository.GetByIdAsync(ticket.SprintId, cancellationToken)
                ?? throw new NotFoundException(nameof(Sprint), ticket.SprintId);

            var branchName = ticket.BranchName;

            await using (await GitRepositoryLock.AcquireAsync(project.RepositoryPath, cancellationToken))
            {
                // Validate before touching Git - UnlinkBranch guards that this is only allowed
                // once the ticket is Cancelled (already true at this point), matching
                // TicketService's own DeleteBranchAsync ordering. Unlike TicketService.CancelAsync,
                // this doesn't need to hold the lock across its own SaveChangesAsync too: Reject
                // only reaches a ForReview ticket, and nothing writes to a ticket's branch while
                // it's sitting ForReview (the pipeline only runs In Progress), so there's no
                // concurrent commit/link this could otherwise race.
                ticket.UnlinkBranch();

                var accessToken = credentialProtector.Unprotect(project.EncryptedAccessToken);
                await gitService.DeleteBranchAsync(project.RepositoryPath, branchName, sprint.BaseBranch, accessToken, cancellationToken);

                await auditLogger.LogActionAsync(AuditEventType.GitBranchDeleted, $"Branch '{branchName}' deleted for rejected ticket '{ticket.Title}'.", cancellationToken);
            }
        }

        await auditLogger.LogActionAsync(AuditEventType.TicketCancelled, $"Ticket '{ticket.Title}' rejected in review.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task ApproveAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ticket.BranchName))
        {
            throw new InvalidOperationException("Ticket has no linked branch to merge.");
        }

        // The authenticated approver's own login name, not the free-text SubmitReviewRequest.ReviewerName
        // (which is only a display label on the Review record) - this is what lands in the merge
        // commit's author signature and message, so the Git history reflects who actually clicked
        // Approve rather than whatever name a caller typed into the review form.
        var approverName = currentUser.Name ?? "Unknown";

        // Fast, cheap pre-check against what we already know: an early, clear error instead of
        // fetching/attempting a merge that the live check below would reject anyway.
        var unresolvedFilePaths = ticket.Conflicts
            .Where(c => c.Status is ConflictStatus.Detected or ConflictStatus.AiResolutionSuggested)
            .Select(c => c.FilePath)
            .ToList();
        if (unresolvedFilePaths.Count > 0)
        {
            throw new UnresolvedConflictsException(unresolvedFilePaths);
        }

        // Every resolved conflict's content, plus the base-branch tip it was prepared against,
        // ready to hand to the real merge attempt below - see IGitService.MergeWithResolutionsAsync
        // and Domain.Entities.Conflict.BaseTipSha.
        var resolvedConflicts = ticket.Conflicts
            .Where(c => c.Status is ConflictStatus.ResolvedManually or ConflictStatus.ResolvedWithAiSuggestion)
            .ToList();
        var resolutions = resolvedConflicts
            .ToDictionary(c => c.FilePath, c => new GitConflictResolution(c.ResolvedContent!, c.BaseTipSha));

        var project = await projectRepository.GetByIdAsync(ticket.ProjectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), ticket.ProjectId);

        var sprint = await sprintRepository.GetByIdAsync(ticket.SprintId, cancellationToken)
            ?? throw new NotFoundException(nameof(Sprint), ticket.SprintId);

        var branchName = ticket.BranchName;
        var targetBranch = sprint.BaseBranch;
        var accessToken = credentialProtector.Unprotect(project.EncryptedAccessToken);

        await using (await GitRepositoryLock.AcquireAsync(project.RepositoryPath, cancellationToken))
        {
            // Fetch first to reduce the odds of merging against a stale local copy of the base
            // branch (e.g. if someone pushed to it directly, outside TeamPilot).
            await gitService.FetchAsync(project.RepositoryPath, accessToken, cancellationToken);

            // The live, authoritative check: even if every known Conflict record says resolved,
            // the base branch may have moved further since it was last checked, so this can still
            // find (and reject on) a file with no resolution available, or one whose resolution
            // is now stale - see IGitService.MergeWithResolutionsAsync. Deliberately outside the
            // transaction below (no DB write happens before this returns), so a stale-resolution
            // reset (see the StaleFilePaths branch) actually persists instead of rolling back
            // together with the failed approval it's reported alongside.
            var mergeResult = await gitService.MergeWithResolutionsAsync(project.RepositoryPath, branchName, targetBranch, resolutions, approverName, cancellationToken);
            if (!mergeResult.Success)
            {
                if (mergeResult.StaleFilePaths.Count > 0)
                {
                    // Reset just the stale ones back to Detected so a reviewer sees them as
                    // needing resolution again, instead of them silently staying marked resolved
                    // while this exception is the only sign anything's wrong.
                    var staleConflicts = resolvedConflicts.Where(c => mergeResult.StaleFilePaths.Contains(c.FilePath)).ToList();
                    foreach (var staleConflict in staleConflicts)
                    {
                        staleConflict.MarkStale();
                    }

                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    throw new StaleConflictResolutionException(mergeResult.StaleFilePaths);
                }

                throw new UnresolvedConflictsException(mergeResult.UnresolvedFilePaths);
            }

            await unitOfWork.ExecuteInTransactionAsync(
                async () =>
                {
                    ticket.Approve();
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                },
                cancellationToken);

            // Pushed after the transaction commits: the local approval already stands even if this
            // push fails transiently, so a push failure here is surfaced as an error but does not
            // roll back the approval.
            await gitService.PushAsync(project.RepositoryPath, targetBranch, accessToken, cancellationToken);
        }

        logger.LogInformation(
            "Ticket {TicketId} in project {ProjectId} approved and merged by {ApproverName}",
            ticket.Id,
            ticket.ProjectId,
            approverName);
    }
}
