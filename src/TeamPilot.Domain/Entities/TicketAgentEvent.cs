using TeamPilot.Domain.Common;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Domain.Entities;

/// <summary>
/// A per-ticket agent lifecycle event ("Research agent started", "Coding agent completed", ...)
/// for the ticket detail page's live agent log - distinct from <see cref="StageExecution"/>
/// (completion-only, undisplayed, used for review/answer feedback threading) and
/// <see cref="TicketQuestion"/> (only raised when the pipeline pauses). References its
/// <see cref="Ticket"/> and <see cref="Agent"/> by id only, matching how those two relate to
/// <see cref="Ticket"/> - not eagerly loaded onto <see cref="Ticket"/>, since a ticket can
/// accumulate an unbounded number of these over its lifetime. See docs/domain.md.
/// </summary>
public class TicketAgentEvent : Entity
{
    public Guid TicketId { get; private set; }

    /// <summary>
    /// The stage's agent this event is about - null only for a <see cref="TicketAgentEventKind.Failed"/>
    /// event raised before any stage ran (e.g. linking the ticket's branch failed).
    /// </summary>
    public Guid? AgentId { get; private set; }

    /// <summary>
    /// The agent's role at event time, snapshotted the same way as
    /// <see cref="TicketAgentAssignment.RoleAtAssignment"/> so a later role change on the agent
    /// doesn't rewrite this event's label. Null exactly when <see cref="AgentId"/> is null.
    /// </summary>
    public AgentRole? Role { get; private set; }

    public TicketAgentEventKind Kind { get; private set; }

    /// <summary>The agent's output (Completed), the question/decision text (Blocked), or the
    /// failure's message (Failed). Null for Started.</summary>
    public string? Result { get; private set; }

    private TicketAgentEvent()
    {
    }

    public static TicketAgentEvent CreateStarted(Guid ticketId, Guid agentId, AgentRole role)
    {
        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("Agent id is required.", nameof(agentId));
        }

        return Create(ticketId, agentId, role, TicketAgentEventKind.Started, result: null);
    }

    public static TicketAgentEvent CreateCompleted(Guid ticketId, Guid agentId, AgentRole role, string result)
    {
        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("Agent id is required.", nameof(agentId));
        }

        return Create(ticketId, agentId, role, TicketAgentEventKind.Completed, result ?? string.Empty);
    }

    public static TicketAgentEvent CreateBlocked(Guid ticketId, Guid agentId, AgentRole role, string result)
    {
        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("Agent id is required.", nameof(agentId));
        }

        return Create(ticketId, agentId, role, TicketAgentEventKind.Blocked, result ?? string.Empty);
    }

    /// <summary>Agent/role are optional - a failure can occur before any stage has run.</summary>
    public static TicketAgentEvent CreateFailed(Guid ticketId, Guid? agentId, AgentRole? role, string result) =>
        Create(ticketId, agentId, role, TicketAgentEventKind.Failed, result ?? string.Empty);

    private static TicketAgentEvent Create(Guid ticketId, Guid? agentId, AgentRole? role, TicketAgentEventKind kind, string? result)
    {
        if (ticketId == Guid.Empty)
        {
            throw new ArgumentException("Ticket id is required.", nameof(ticketId));
        }

        return new TicketAgentEvent
        {
            TicketId = ticketId,
            AgentId = agentId,
            Role = role,
            Kind = kind,
            Result = result,
        };
    }
}
