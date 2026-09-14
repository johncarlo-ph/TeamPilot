using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using TeamPilot.Application.Common.Interfaces;

namespace TeamPilot.API.Controllers;

/// <summary>
/// Backs the board and ticket-detail pages' real-time refresh (see
/// <see cref="IProjectEventBroadcaster"/>) - replaces the fixed-interval polling those pages used
/// before this existed (see docs/frontend.md). One long-lived <c>text/event-stream</c> connection
/// per open board/ticket-detail page, not a request-per-poll-tick.
/// </summary>
[ApiController]
public class ProjectEventsController(IProjectAccessGuard projectAccessGuard, IProjectEventBroadcaster eventBroadcaster) : ControllerBase
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    // Comment lines (": ...") are valid, ignorable SSE frames - sent periodically so an
    // intermediary (reverse proxy, load balancer) that idle-times-out a connection with no
    // traffic doesn't silently drop this one.
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    [HttpGet("api/projects/{projectId:guid}/events")]
    public async Task Stream(Guid projectId, CancellationToken cancellationToken)
    {
        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        // Reverse proxies (nginx) buffer a response by default, which would hold every event
        // until the connection closes instead of delivering it live - this header is nginx's own
        // documented opt-out; harmless for proxies that don't recognize it.
        Response.Headers["X-Accel-Buffering"] = "no";

        var bufferingFeature = HttpContext.Features.Get<IHttpResponseBodyFeature>();
        bufferingFeature?.DisableBuffering();

        try
        {
            await foreach (var projectEvent in MergeWithHeartbeat(projectId, cancellationToken))
            {
                await Response.WriteAsync(projectEvent, cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The client navigated away or the connection dropped - HttpContext.RequestAborted
            // firing is the expected, non-exceptional way this stream ends.
        }
    }

    private async IAsyncEnumerable<string> MergeWithHeartbeat(Guid projectId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var subscription = eventBroadcaster.Subscribe(projectId, cancellationToken).GetAsyncEnumerator(cancellationToken);
        var nextEvent = subscription.MoveNextAsync().AsTask();

        try
        {
            while (true)
            {
                var completed = await Task.WhenAny(nextEvent, Task.Delay(HeartbeatInterval, cancellationToken));

                if (completed == nextEvent)
                {
                    if (!await nextEvent)
                    {
                        yield break;
                    }

                    yield return $"data: {JsonSerializer.Serialize(subscription.Current, SerializerOptions)}\n\n";
                    nextEvent = subscription.MoveNextAsync().AsTask();
                }
                else
                {
                    // Throws if it was cancellationToken (not the interval) that completed the
                    // delay, instead of a blind yield that would send a heartbeat post-cancellation.
                    await completed;
                    yield return ": heartbeat\n\n";
                }
            }
        }
        finally
        {
            // The compiler-generated enumerator from ProjectEventBroadcaster.Subscribe doesn't
            // support DisposeAsync running while a MoveNextAsync it returned is still pending -
            // doing so throws NotSupportedException ("Specific method is not supported"). Whatever
            // path got us here (cancellation, an exception, or normal yield break) may leave
            // `nextEvent` outstanding, so drain it first; its own cancellationToken means this
            // won't hang.
            try
            {
                await nextEvent;
            }
            catch
            {
                // Disposing regardless of how the pending MoveNextAsync completed.
            }

            await subscription.DisposeAsync();
        }
    }
}
