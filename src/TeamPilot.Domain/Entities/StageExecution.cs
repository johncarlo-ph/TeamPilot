using TeamPilot.Domain.Common;

namespace TeamPilot.Domain.Entities;

/// <summary>
/// An immutable fact record of one agent's output for one ticket, created once per stage
/// invocation. References its <see cref="Ticket"/> and <see cref="Agent"/> by id only, matching
/// how <see cref="Commit"/>/<see cref="Review"/>/<see cref="Conflict"/> relate to
/// <see cref="Ticket"/> - see docs/domain.md for why this one isn't eagerly loaded onto
/// <see cref="Ticket"/> the way those are.
/// </summary>
public class StageExecution : Entity
{
    public Guid TicketId { get; private set; }

    public Guid AgentId { get; private set; }

    public string Output { get; private set; } = string.Empty;

    private StageExecution()
    {
    }

    public static StageExecution Create(Guid ticketId, Guid agentId, string output)
    {
        if (ticketId == Guid.Empty)
        {
            throw new ArgumentException("Ticket id is required.", nameof(ticketId));
        }

        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("Agent id is required.", nameof(agentId));
        }

        return new StageExecution
        {
            TicketId = ticketId,
            AgentId = agentId,
            Output = output ?? string.Empty,
        };
    }
}
