namespace TeamPilot.Application.Common;

/// <summary>
/// A refetch signal delivered over <see cref="Interfaces.IProjectEventBroadcaster"/> - carries no
/// state of its own beyond what changed and, when known, which ticket. Consumers (the board and
/// ticket-detail pages) already know how to fetch their own authoritative state; this only tells
/// them when to do it again, so the event shape never has to be kept in sync with the DTOs it's
/// about, the way a full state-push design would.
/// </summary>
/// <param name="Type">One of <see cref="ProjectEventTypes"/>.</param>
/// <param name="ProjectId">The project whose subscribers should react.</param>
/// <param name="TicketId">The ticket this event is about, when the change is ticket-scoped.</param>
public sealed record ProjectEvent(string Type, Guid ProjectId, Guid? TicketId, DateTime OccurredAtUtc);

public static class ProjectEventTypes
{
    /// <summary>A ticket's status, review, commit, branch, or pipeline-running state changed.</summary>
    public const string TicketChanged = "TicketChanged";

    /// <summary>A <c>TicketQuestion</c> was created (question/decision/failure) or answered.</summary>
    public const string TicketQuestionChanged = "TicketQuestionChanged";

    /// <summary>A <c>TicketAgentEvent</c> was recorded - a pipeline stage started, or ended
    /// (completed/blocked/failed) - for the ticket detail page's live agent log.</summary>
    public const string TicketAgentEventLogged = "TicketAgentEventLogged";
}
