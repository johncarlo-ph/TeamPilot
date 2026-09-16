using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Common;

/// <summary>
/// A refetch signal delivered over <see cref="Interfaces.IProjectEventBroadcaster"/> - carries no
/// state of its own beyond what changed and, when known, which ticket. Consumers (the board and
/// ticket-detail pages) already know how to fetch their own authoritative state; this only tells
/// them when to do it again, so the event shape never has to be kept in sync with the DTOs it's
/// about, the way a full state-push design would.
///
/// <see cref="CloneProgress"/> is the one deliberate exception: a project card watching
/// <see cref="ProjectEventTypes.ProjectCloneProgress"/> needs live numbers to draw a progress bar,
/// and there's no separate "fetch progress" endpoint to refetch from (progress isn't persisted).
/// </summary>
/// <param name="Type">One of <see cref="ProjectEventTypes"/>.</param>
/// <param name="ProjectId">The project whose subscribers should react.</param>
/// <param name="TicketId">The ticket this event is about, when the change is ticket-scoped.</param>
/// <param name="CloneProgress">Populated only for <see cref="ProjectEventTypes.ProjectCloneProgress"/>.</param>
public sealed record ProjectEvent(string Type, Guid ProjectId, Guid? TicketId, DateTime OccurredAtUtc, CloneProgressPayload? CloneProgress = null);

public static class ProjectEventTypes
{
    /// <summary>A ticket's status, review, commit, branch, or pipeline-running state changed.</summary>
    public const string TicketChanged = "TicketChanged";

    /// <summary>A <c>TicketQuestion</c> was created (question/decision/failure) or answered.</summary>
    public const string TicketQuestionChanged = "TicketQuestionChanged";

    /// <summary>A <c>TicketAgentEvent</c> was recorded - a pipeline stage started, or ended
    /// (completed/blocked/failed) - for the ticket detail page's live agent log.</summary>
    public const string TicketAgentEventLogged = "TicketAgentEventLogged";

    /// <summary>A project's detached initial clone (see <c>Projects.ProjectService.CreateAsync</c>)
    /// reported progress, or finished (successfully or not) - see <c>ProjectEvent.CloneProgress</c>.</summary>
    public const string ProjectCloneProgress = "ProjectCloneProgress";
}

/// <summary>Live clone progress/outcome for one project, carried on a
/// <see cref="ProjectEventTypes.ProjectCloneProgress"/> event. <paramref name="TotalObjects"/> is
/// 0 while still indeterminate (see <c>Git.GitCloneProgress</c>). <paramref name="Status"/> is
/// <see cref="ProjectStatus.Cloning"/> for every in-progress update and flips to
/// <see cref="ProjectStatus.Ready"/>/<see cref="ProjectStatus.Failed"/> on the final event, at
/// which point <paramref name="ErrorMessage"/> is populated for a failure.</summary>
public sealed record CloneProgressPayload(
    long ReceivedObjects,
    long TotalObjects,
    long ReceivedBytes,
    ProjectStatus Status,
    string? ErrorMessage);
