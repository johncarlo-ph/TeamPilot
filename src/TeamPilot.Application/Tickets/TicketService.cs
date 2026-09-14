using FluentValidation;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Extensions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Git;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Tickets.Dtos;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Tickets;

public sealed class TicketService(
    ITicketRepository ticketRepository,
    IProjectRepository projectRepository,
    IGitService gitService,
    IGitCredentialProtector credentialProtector,
    IProjectAccessGuard projectAccessGuard,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork,
    IPipelineRunTracker pipelineRunTracker,
    IValidator<CreateTicketRequest> createValidator,
    IValidator<CreateBranchRequest> createBranchValidator,
    IValidator<CancelTicketRequest> cancelValidator) : ITicketService
{
    public async Task<TicketDto> CreateAsync(Guid projectId, CreateTicketRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.EnsureValidAsync(request, cancellationToken);
        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);

        _ = await projectRepository.GetByIdAsync(projectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), projectId);

        var ticket = Ticket.Create(projectId, request.Title, request.Description);
        await ticketRepository.AddAsync(ticket, cancellationToken);

        await auditLogger.LogActionAsync(AuditEventType.TicketCreated, $"Ticket '{ticket.Title}' created.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TicketMappings.ToDto(ticket, pipelineRunTracker.IsRunning(ticket.Id));
    }

    public async Task<TicketDetailDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ticket = await ticketRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), id);

        await projectAccessGuard.EnsureAccessAsync(ticket.ProjectId, cancellationToken);

        return TicketMappings.ToDetailDto(ticket, pipelineRunTracker.IsRunning(ticket.Id));
    }

    public async Task<IReadOnlyList<TicketDto>> ListAsync(Guid projectId, TicketStatus? status, CancellationToken cancellationToken = default)
    {
        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);

        var tickets = await ticketRepository.ListAsync(projectId, status, cancellationToken);
        return tickets.Select(t => TicketMappings.ToDto(t, pipelineRunTracker.IsRunning(t.Id))).ToList();
    }

    public async Task<TicketDto> MoveToReviewAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ticket = await ticketRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), id);

        await projectAccessGuard.EnsureAccessAsync(ticket.ProjectId, cancellationToken);

        ticket.MoveToReview();

        await auditLogger.LogActionAsync(AuditEventType.TicketMovedToReview, $"Ticket '{ticket.Title}' moved to review.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

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

        return TicketMappings.ToDto(ticket, pipelineRunTracker.IsRunning(ticket.Id));
    }

    // Validate before touching Git - UnlinkBranch guards that this is only allowed once the
    // ticket is Cancelled, and there's no point deleting the remote branch if that check is
    // about to fail anyway. Callers hold the GitRepositoryLock for project.RepositoryPath already.
    private async Task DeleteLinkedBranchAsync(Ticket ticket, Project project, CancellationToken cancellationToken)
    {
        var branchName = ticket.BranchName!;
        ticket.UnlinkBranch();

        var accessToken = credentialProtector.Unprotect(project.EncryptedAccessToken);
        await gitService.DeleteBranchAsync(project.RepositoryPath, branchName, project.BaseBranch, accessToken, cancellationToken);

        await auditLogger.LogActionAsync(AuditEventType.GitBranchDeleted, $"Branch '{branchName}' deleted for ticket '{ticket.Title}'.", cancellationToken);
    }

    public async Task<TicketDto> LinkBranchAsync(Guid ticketId, string branchName, CancellationToken cancellationToken = default)
    {
        await createBranchValidator.EnsureValidAsync(new CreateBranchRequest(ticketId, branchName), cancellationToken);

        var ticket = await ticketRepository.GetByIdAsync(ticketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), ticketId);

        await projectAccessGuard.EnsureAccessAsync(ticket.ProjectId, cancellationToken);

        var project = await projectRepository.GetByIdAsync(ticket.ProjectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), ticket.ProjectId);

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
            await gitService.EnsureBranchAsync(project.RepositoryPath, branchName, project.BaseBranch, cancellationToken);
            await gitService.PushAsync(project.RepositoryPath, branchName, accessToken, cancellationToken);
        }

        ticket.LinkBranch(branchName);

        await auditLogger.LogActionAsync(AuditEventType.GitBranchCreated, $"Branch '{branchName}' created for ticket '{ticket.Title}'.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

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
