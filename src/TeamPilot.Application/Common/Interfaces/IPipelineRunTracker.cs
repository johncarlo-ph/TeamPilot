namespace TeamPilot.Application.Common.Interfaces;

/// <summary>
/// Tracks, in memory, which tickets currently have a pipeline run actually executing in the
/// background (see <c>IOrchestrationService.RunPipelineDetached</c>) - <c>Ticket.Status</c> alone
/// can't distinguish this: a ticket sits <c>InProgress</c> both while a run is actively executing
/// and while it's simply idle, waiting for a human to manually trigger one (e.g. via a "Run
/// Pipeline" button). Deliberately in-memory rather than persisted: the actual background work
/// dies with the process too, so a restart can never leave this stuck reporting "running" for a
/// run that no longer exists - the classic failure mode of a persisted flag with no crash-safe
/// cleanup.
/// </summary>
public interface IPipelineRunTracker
{
    void MarkRunning(Guid ticketId);

    void MarkFinished(Guid ticketId);

    bool IsRunning(Guid ticketId);
}
