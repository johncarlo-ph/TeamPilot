using TeamPilot.Domain.Common;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Domain.Entities;

/// <summary>
/// A non-blocking, human-readable note a pipeline stage chose to leave behind for whoever reviews
/// the ticket - a summary of what it did, an assumption it made, or a limitation/follow-up worth
/// flagging (see the <c>NOTES:</c> marker every stage's prompt contract offers in
/// <c>OrchestrationService.BuildStagePrompt</c>). Never gates anything, unlike
/// <see cref="TicketQuestion"/> - it's surfaced read-only on the ticket detail page, most usefully
/// once the ticket reaches <see cref="TicketStatus.Done"/> and there's no more agent output to dig
/// through. References its <see cref="Ticket"/> and <see cref="Agent"/> by id only, matching how
/// <see cref="TicketProjectInstruction"/>/<see cref="TicketAgentEvent"/> relate to
/// <see cref="Ticket"/> - not eagerly loaded onto <see cref="Ticket"/>, since a ticket can
/// accumulate an unbounded number of these over its lifetime. See docs/domain.md.
/// </summary>
public class TicketPipelineNote : Entity
{
    public Guid TicketId { get; private set; }

    /// <summary>The stage's agent that left this note.</summary>
    public Guid? AgentId { get; private set; }

    /// <summary>The agent's role at the time, snapshotted the same way as
    /// <see cref="TicketAgentEvent.Role"/> so a later role change on the agent doesn't rewrite
    /// this note's label.</summary>
    public AgentRole? Role { get; private set; }

    public string Text { get; private set; } = string.Empty;

    private TicketPipelineNote()
    {
    }

    public static TicketPipelineNote Create(Guid ticketId, Guid? agentId, AgentRole? role, string text)
    {
        if (ticketId == Guid.Empty)
        {
            throw new ArgumentException("Ticket id is required.", nameof(ticketId));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Note text is required.", nameof(text));
        }

        return new TicketPipelineNote
        {
            TicketId = ticketId,
            AgentId = agentId,
            Role = role,
            Text = text,
        };
    }
}
