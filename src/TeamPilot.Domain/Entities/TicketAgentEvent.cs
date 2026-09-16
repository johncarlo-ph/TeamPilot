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

    /// <summary>The LLM call's prompt/completion token usage for this stage attempt - set for
    /// Completed/Blocked (both always involve a real LLM call), null for Started (nothing called
    /// yet) and Failed (a Git-only failure never calls the LLM at all, and an LLM-call failure
    /// has no usage to report from a call that didn't succeed).</summary>
    public int? InputTokens { get; private set; }

    public int? OutputTokens { get; private set; }

    /// <summary>Wall-clock time this stage attempt took, from its Started event to this one -
    /// null only for Started itself.</summary>
    public int? DurationMs { get; private set; }

    private TicketAgentEvent()
    {
    }

    public static TicketAgentEvent CreateStarted(Guid ticketId, Guid agentId, AgentRole role)
    {
        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("Agent id is required.", nameof(agentId));
        }

        return Create(ticketId, agentId, role, TicketAgentEventKind.Started, result: null, inputTokens: null, outputTokens: null, durationMs: null);
    }

    public static TicketAgentEvent CreateCompleted(Guid ticketId, Guid agentId, AgentRole role, string result, int inputTokens, int outputTokens, int durationMs)
    {
        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("Agent id is required.", nameof(agentId));
        }

        return Create(ticketId, agentId, role, TicketAgentEventKind.Completed, result ?? string.Empty, inputTokens, outputTokens, durationMs);
    }

    public static TicketAgentEvent CreateBlocked(Guid ticketId, Guid agentId, AgentRole role, string result, int inputTokens, int outputTokens, int durationMs)
    {
        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("Agent id is required.", nameof(agentId));
        }

        return Create(ticketId, agentId, role, TicketAgentEventKind.Blocked, result ?? string.Empty, inputTokens, outputTokens, durationMs);
    }

    /// <summary>Agent/role are optional - a failure can occur before any stage has run. No token
    /// counts - see <see cref="InputTokens"/>.</summary>
    public static TicketAgentEvent CreateFailed(Guid ticketId, Guid? agentId, AgentRole? role, string result, int? durationMs) =>
        Create(ticketId, agentId, role, TicketAgentEventKind.Failed, result ?? string.Empty, inputTokens: null, outputTokens: null, durationMs);

    private static TicketAgentEvent Create(Guid ticketId, Guid? agentId, AgentRole? role, TicketAgentEventKind kind, string? result, int? inputTokens, int? outputTokens, int? durationMs)
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
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            DurationMs = durationMs,
        };
    }
}
