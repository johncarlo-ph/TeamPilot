using TeamPilot.Application.Tickets.Dtos;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Tickets;

public interface ITicketService
{
    Task<TicketDto> CreateAsync(Guid projectId, CreateTicketRequest request, CancellationToken cancellationToken = default);

    Task<TicketDetailDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TicketDto>> ListAsync(Guid projectId, TicketStatus? status, CancellationToken cancellationToken = default);

    Task<TicketDto> MoveToReviewAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the branch exists in the ticket's project's sandbox Git repository and links
    /// it to the ticket.
    /// </summary>
    Task<TicketDto> LinkBranchAsync(Guid ticketId, string branchName, CancellationToken cancellationToken = default);
}
