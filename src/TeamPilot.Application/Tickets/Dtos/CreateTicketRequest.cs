namespace TeamPilot.Application.Tickets.Dtos;

public sealed record CreateTicketRequest(string Title, string? Description, string AcceptanceCriteria);
