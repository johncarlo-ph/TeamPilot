using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using TeamPilot.Application.Common;
using TeamPilot.Application.Common.Interfaces;

namespace TeamPilot.Infrastructure.RealTime;

/// <summary>
/// One bounded <see cref="Channel{T}"/> per active SSE connection, grouped by project so
/// <see cref="Publish"/> can fan out without touching subscribers of any other project.
/// </summary>
public sealed class ProjectEventBroadcaster : IProjectEventBroadcaster
{
    // A subscriber only ever needs to know that something changed, not to see every change
    // queued up (see ProjectEvent's own doc comment) - so a small bounded channel that drops the
    // oldest entry under backpressure (a slow or stalled reader) is preferable to an unbounded
    // one that could otherwise grow without limit for a connection nobody is draining.
    private const int ChannelCapacity = 16;

    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<ProjectEvent>>> _subscribersByProject = new();

    public void Publish(Guid projectId, ProjectEvent projectEvent)
    {
        if (!_subscribersByProject.TryGetValue(projectId, out var subscribers))
        {
            return;
        }

        foreach (var channel in subscribers.Values)
        {
            // Always succeeds (or drops the oldest queued event) rather than blocking - a
            // publish must never wait on a subscriber that isn't reading.
            channel.Writer.TryWrite(projectEvent);
        }
    }

    public async IAsyncEnumerable<ProjectEvent> Subscribe(Guid projectId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<ProjectEvent>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        var subscriptionId = Guid.NewGuid();
        var subscribers = _subscribersByProject.GetOrAdd(projectId, static _ => new ConcurrentDictionary<Guid, Channel<ProjectEvent>>());
        subscribers[subscriptionId] = channel;

        try
        {
            await foreach (var projectEvent in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return projectEvent;
            }
        }
        finally
        {
            // Unregister on disconnect (cancellationToken cancelled) so Publish stops holding a
            // reference to a channel nobody will ever read from again.
            subscribers.TryRemove(subscriptionId, out _);
        }
    }
}
