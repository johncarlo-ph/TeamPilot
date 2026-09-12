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

        return TicketMappings.ToDto(ticket);
    }

    public async Task<TicketDetailDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ticket = await ticketRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), id);

        await projectAccessGuard.EnsureAccessAsync(ticket.ProjectId, cancellationToken);

        return TicketMappings.ToDetailDto(ticket);
    }

    public async Task<IReadOnlyList<TicketDto>> ListAsync(Guid projectId, TicketStatus? status, CancellationToken cancellationToken = default)
    {
        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);

        var tickets = await ticketRepository.ListAsync(projectId, status, cancellationToken);
        return tickets.Select(TicketMappings.ToDto).ToList();
    }

    public async Task<TicketDto> MoveToReviewAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ticket = await ticketRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), id);

        await projectAccessGuard.EnsureAccessAsync(ticket.ProjectId, cancellationToken);

        ticket.MoveToReview();

        await auditLogger.LogActionAsync(AuditEventType.TicketMovedToReview, $"Ticket '{ticket.Title}' moved to review.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TicketMappings.ToDto(ticket);
    }

    public async Task<TicketDto> CancelAsync(Guid id, CancelTicketRequest request, CancellationToken cancellationToken = default)
    {
        await cancelValidator.EnsureValidAsync(request, cancellationToken);

        var ticket = await ticketRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), id);

        await projectAccessGuard.EnsureAccessAsync(ticket.ProjectId, cancellationToken);

        ticket.Cancel(request.Reason);

        await auditLogger.LogActionAsync(AuditEventType.TicketCancelled, $"Ticket '{ticket.Title}' cancelled.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TicketMappings.ToDto(ticket);
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

        var branchName = ticket.BranchName;

        // Validate before touching Git - UnlinkBranch guards that this is only allowed once the
        // ticket is Cancelled, and there's no point deleting the remote branch if that check is
        // about to fail anyway.
        ticket.UnlinkBranch();

        var project = await projectRepository.GetByIdAsync(ticket.ProjectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), ticket.ProjectId);

        var accessToken = credentialProtector.Unprotect(project.EncryptedAccessToken);
        await gitService.DeleteBranchAsync(project.RepositoryPath, branchName, project.BaseBranch, accessToken, cancellationToken);

        await auditLogger.LogActionAsync(AuditEventType.GitBranchDeleted, $"Branch '{branchName}' deleted for ticket '{ticket.Title}'.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TicketMappings.ToDto(ticket);
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

        // Fetch first so the new branch is cut from the remote's current tip of the base
        // branch, not a possibly-stale local one.
        await gitService.FetchAsync(project.RepositoryPath, accessToken, cancellationToken);
        await gitService.EnsureBranchAsync(project.RepositoryPath, branchName, project.BaseBranch, cancellationToken);
        await gitService.PushAsync(project.RepositoryPath, branchName, accessToken, cancellationToken);

        ticket.LinkBranch(branchName);

        await auditLogger.LogActionAsync(AuditEventType.GitBranchCreated, $"Branch '{branchName}' created for ticket '{ticket.Title}'.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TicketMappings.ToDto(ticket);
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

        // Fetch first so the check reflects the remote's current refs, not a possibly-stale
        // local view.
        await gitService.FetchAsync(project.RepositoryPath, accessToken, cancellationToken);

        return await gitService.BranchExistsAsync(project.RepositoryPath, branchName, cancellationToken);
    }
}
