namespace TeamPilot.Application.Common.Exceptions;

/// <summary>
/// Thrown when a pipeline start or manual branch link is attempted on a backlog ticket (one with
/// no <see cref="Domain.Entities.Ticket.SprintId"/> yet) - both need a sprint's
/// <see cref="Domain.Entities.Sprint.BaseBranch"/> to cut the ticket's branch from.
/// </summary>
public sealed class TicketNotAssignedToSprintException(string ticketTitle)
    : Exception($"Ticket '{ticketTitle}' must be assigned to a sprint before its pipeline can start or a branch can be linked.")
{
}
