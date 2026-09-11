using FluentValidation;
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
    IProjectAccessGuard projectAccessGuard,
    IUnitOfWork unitOfWork,
    IValidator<CreateTicketRequest> createValidator,
    IValidator<CreateBranchRequest> createBranchValidator) : ITicketService
{
    public async Task<TicketDto> CreateAsync(Guid projectId, CreateTicketRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.EnsureValidAsync(request, cancellationToken);
        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);

        _ = await projectRepository.GetByIdAsync(projectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), projectId);

        var ticket = Ticket.Create(projectId, request.Title, request.Description);
        await ticketRepository.AddAsync(ticket, cancellationToken);
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

        await gitService.EnsureBranchAsync(project.RepositoryPath, branchName, cancellationToken);
        ticket.LinkBranch(branchName);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TicketMappings.ToDto(ticket);
    }
}
