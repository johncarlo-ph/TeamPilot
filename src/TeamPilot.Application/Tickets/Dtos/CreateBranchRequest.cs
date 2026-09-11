namespace TeamPilot.Application.Tickets.Dtos;

public sealed record CreateBranchRequest(Guid TicketId, string BranchName);
