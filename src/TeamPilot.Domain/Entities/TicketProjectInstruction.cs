using TeamPilot.Domain.Common;

namespace TeamPilot.Domain.Entities;

/// <summary>
/// A non-blocking, human-readable note raised by a Research or Design pipeline stage when a
/// ticket's description references another project (see <c>MentionParser</c>) and the stage
/// decides that OTHER project itself needs a change. Never triggers an automated cross-repo
/// write - it's surfaced read-only on the ticket detail page for a human to act on manually.
/// References its <see cref="Ticket"/>, <see cref="Agent"/>, and the referenced
/// <see cref="Project"/> by id only, matching how <see cref="TicketQuestion"/>/
/// <see cref="StageExecution"/> relate to <see cref="Ticket"/> - not eagerly loaded onto
/// <see cref="Ticket"/>, since a ticket can accumulate an unbounded number of these over its
/// lifetime. See docs/domain.md.
/// </summary>
public class TicketProjectInstruction : Entity
{
    public Guid TicketId { get; private set; }

    /// <summary>The Research/Design stage's agent that raised this.</summary>
    public Guid? AgentId { get; private set; }

    /// <summary>The OTHER project this instruction is about - never the ticket's own project.</summary>
    public Guid ReferencedProjectId { get; private set; }

    public string Text { get; private set; } = string.Empty;

    private TicketProjectInstruction()
    {
    }

    public static TicketProjectInstruction Create(Guid ticketId, Guid? agentId, Guid referencedProjectId, string text)
    {
        if (ticketId == Guid.Empty)
        {
            throw new ArgumentException("Ticket id is required.", nameof(ticketId));
        }

        if (referencedProjectId == Guid.Empty)
        {
            throw new ArgumentException("Referenced project id is required.", nameof(referencedProjectId));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Instruction text is required.", nameof(text));
        }

        return new TicketProjectInstruction
        {
            TicketId = ticketId,
            AgentId = agentId,
            ReferencedProjectId = referencedProjectId,
            Text = text,
        };
    }
}
