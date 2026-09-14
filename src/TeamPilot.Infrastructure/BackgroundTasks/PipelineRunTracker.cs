using System.Collections.Concurrent;
using TeamPilot.Application.Common.Interfaces;

namespace TeamPilot.Infrastructure.BackgroundTasks;

/// <summary>
/// Reference-counts running pipelines per ticket rather than a plain flag, so an overlapping
/// second run for the same ticket (e.g. two browser tabs both triggering one before either has
/// seen the other's <c>TicketDto.PipelineRunning</c>) isn't reported as finished the moment the
/// first of the two completes.
/// </summary>
public sealed class PipelineRunTracker : IPipelineRunTracker
{
    private readonly ConcurrentDictionary<Guid, int> _runningCounts = new();

    public void MarkRunning(Guid ticketId) =>
        _runningCounts.AddOrUpdate(ticketId, 1, (_, count) => count + 1);

    public void MarkFinished(Guid ticketId) =>
        _runningCounts.AddOrUpdate(ticketId, 0, (_, count) => Math.Max(0, count - 1));

    public bool IsRunning(Guid ticketId) =>
        _runningCounts.TryGetValue(ticketId, out var count) && count > 0;
}
