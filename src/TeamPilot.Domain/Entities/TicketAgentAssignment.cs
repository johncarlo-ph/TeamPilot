using TeamPilot.Domain.Common;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Domain.Entities;

/// <summary>
/// Records that an <see cref="Agent"/> was assigned to a <see cref="Ticket"/>, snapshotting the
/// agent's role at assignment time so later role changes on the agent don't rewrite history.
/// </summary>
public class TicketAgentAssignment : Entity
{
    public Guid TicketId { get; private set; }

    public Guid AgentId { get; private set; }

    public AgentRole RoleAtAssignment { get; private set; }

    public DateTime AssignedAtUtc { get; private set; }

    private TicketAgentAssignment()
    {
    }

    internal static TicketAgentAssignment Create(Guid ticketId, Guid agentId, AgentRole roleAtAssignment)
    {
        return new TicketAgentAssignment
        {
            TicketId = ticketId,
            AgentId = agentId,
            RoleAtAssignment = roleAtAssignment,
            AssignedAtUtc = DateTime.UtcNow,
        };
    }
}
