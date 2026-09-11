using FluentValidation;
using Microsoft.Extensions.Logging;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Extensions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Git;
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
    IPipelineService pipelineService,
    IProjectAccessGuard projectAccessGuard,
    ICurrentUserContext currentUser,
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

        if (request.Decision == ReviewDecision.Approve
            && !currentUser.IsInRole(UserRole.Admin)
            && !currentUser.IsInRole(UserRole.Developer))
        {
            throw new ForbiddenException("Only Developers or Admins may approve tickets.");
        }

        var review = Review.Create(ticket.Id, request.ReviewerName, request.Decision, request.Comments);
        ticket.RecordReview(review);

        switch (request.Decision)
        {
            case ReviewDecision.Approve:
                await ApproveAsync(ticket, request.ReviewerName, cancellationToken);
                break;

            case ReviewDecision.RequestChanges:
                ticket.RequestChanges();
                await unitOfWork.SaveChangesAsync(cancellationToken);
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

    private async Task ApproveAsync(Ticket ticket, string reviewerName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ticket.BranchName))
        {
            throw new InvalidOperationException("Ticket has no linked branch to merge.");
        }

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
                await gitService.MergeBranchAsync(project.RepositoryPath, branchName, targetBranch, reviewerName, cancellationToken);
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
