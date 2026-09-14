namespace TeamPilot.Application.Common.Interfaces;

/// <summary>
/// In-process fan-out for <see cref="ProjectEvent"/>s, backing the project-scoped SSE stream
/// (<c>GET /api/projects/{projectId}/events</c>) that the board and ticket-detail pages consume
/// in place of their old fixed-interval polling. Deliberately in-memory, not durable, like
/// <see cref="IPipelineRunTracker"/>: a subscriber that isn't connected when an event is
/// published simply relies on the caller's own next GET to pick up the current state, so there's
/// nothing to replay after a restart. A true singleton so it's reachable both from a normal
/// per-request scope and from a detached background scope (see
/// <c>IBackgroundTaskRunner</c>/<c>OrchestrationService.RunPipelineDetached</c>), the same
/// reasoning that makes <see cref="IPipelineRunTracker"/> a singleton too.
/// </summary>
public interface IProjectEventBroadcaster
{
    /// <summary>Fans <paramref name="projectEvent"/> out to every subscriber currently
    /// listening for <paramref name="projectId"/>. A no-op if nobody is subscribed.</summary>
    void Publish(Guid projectId, ProjectEvent projectEvent);

    /// <summary>Streams events for <paramref name="projectId"/> until <paramref name="cancellationToken"/>
    /// is cancelled (the client disconnected). Each call opens its own independent subscription.</summary>
    IAsyncEnumerable<ProjectEvent> Subscribe(Guid projectId, CancellationToken cancellationToken);
}
