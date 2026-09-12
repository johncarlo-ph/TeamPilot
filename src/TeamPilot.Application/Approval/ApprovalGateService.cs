using FluentValidation;
using Microsoft.Extensions.Logging;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Extensions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Git;
using TeamPilot.Application.Orchestration;
using TeamPilot.Application.Pipelines;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Reviews.Dtos;
using TeamPilot.Application.Tickets;
using TeamPilot.Application.Tickets.Dtos;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Approval;

public sealed class ApprovalGateService(
    ITicketRepository ticketRepository,
    IProjectRepository projectRepository,
    IGitService gitService,
    IGitCredentialProtector credentialProtector,
    IOrchestrationService orchestrationService,
    IPipelineService pipelineService,
    IProjectAccessGuard projectAccessGuard,
    ICurrentUserContext currentUser,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork,
    IValidator<SubmitReviewRequest> validator,
    ILogger<ApprovalGateService> logger) : IApprovalGateService
{
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
                await ApproveAsync(ticket, request.ReviewerName, cancellationToken);
                break;

            case ReviewDecision.RequestChanges:
                ticket.RequestChanges();
                await unitOfWork.SaveChangesAsync(cancellationToken);

                // Immediately re-run the workflow rather than leaving the ticket sitting In
                // Progress until someone manually clicks Run Pipeline - RunPipelineAsync is
                // idempotent/re-entrant by design (see IOrchestrationService), so calling it
                // again here is safe.
                await orchestrationService.RunPipelineAsync(ticket.Id, cancellationToken);
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

        return TicketMappings.ToDto(ticket);
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

            var branchName = ticket.BranchName;

            // Validate before touching Git - UnlinkBranch guards that this is only allowed once
            // the ticket is Cancelled (already true at this point), matching TicketService's own
            // DeleteBranchAsync ordering.
            ticket.UnlinkBranch();

            var accessToken = credentialProtector.Unprotect(project.EncryptedAccessToken);
            await gitService.DeleteBranchAsync(project.RepositoryPath, branchName, project.BaseBranch, accessToken, cancellationToken);

            await auditLogger.LogActionAsync(AuditEventType.GitBranchDeleted, $"Branch '{branchName}' deleted for rejected ticket '{ticket.Title}'.", cancellationToken);
        }

        await auditLogger.LogActionAsync(AuditEventType.TicketCancelled, $"Ticket '{ticket.Title}' rejected in review.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task ApproveAsync(Ticket ticket, string reviewerName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ticket.BranchName))
        {
            throw new InvalidOperationException("Ticket has no linked branch to merge.");
        }

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

        // Every resolved conflict's content, ready to hand to the real merge attempt below - see
        // IGitService.MergeWithResolutionsAsync.
        var resolutions = ticket.Conflicts
            .Where(c => c.Status is ConflictStatus.ResolvedManually or ConflictStatus.ResolvedWithAiSuggestion)
            .ToDictionary(c => c.FilePath, c => c.ResolvedContent!);

        var project = await projectRepository.GetByIdAsync(ticket.ProjectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), ticket.ProjectId);

        var branchName = ticket.BranchName;
        var targetBranch = project.BaseBranch;
        var accessToken = credentialProtector.Unprotect(project.EncryptedAccessToken);

        // Fetch first to reduce the odds of merging against a stale local copy of the base
        // branch (e.g. if someone pushed to it directly, outside TeamPilot).
        await gitService.FetchAsync(project.RepositoryPath, accessToken, cancellationToken);

        await unitOfWork.ExecuteInTransactionAsync(
            async () =>
            {
                // The live, authoritative check: even if every known Conflict record says
                // resolved, the base branch may have moved further since it was last checked, so
                // this can still find (and reject on) a file with no resolution available - see
                // IGitService.MergeWithResolutionsAsync.
                var mergeResult = await gitService.MergeWithResolutionsAsync(project.RepositoryPath, branchName, targetBranch, resolutions, reviewerName, cancellationToken);
                if (!mergeResult.Success)
                {
                    throw new UnresolvedConflictsException(mergeResult.UnresolvedFilePaths);
                }

                ticket.Approve();
                await unitOfWork.SaveChangesAsync(cancellationToken);
            },
            cancellationToken);

        // Pushed after the transaction commits: the local approval already stands even if this
        // push fails transiently, so a push failure here is surfaced as an error but does not
        // roll back the approval.
        await gitService.PushAsync(project.RepositoryPath, targetBranch, accessToken, cancellationToken);

        logger.LogInformation(
            "Ticket {TicketId} in project {ProjectId} approved and merged by {ReviewerName}; triggering pipeline run",
            ticket.Id,
            ticket.ProjectId,
            reviewerName);

        await pipelineService.TriggerAsync(ticket.ProjectId, ticket.Id, $"Merge of ticket '{ticket.Title}'", cancellationToken);
    }
}
