using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TeamPilot.Application.Common.Interfaces;

namespace TeamPilot.Infrastructure.BackgroundTasks;

/// <summary>
/// Fires <see cref="Run"/>'s work on the thread pool inside a fresh DI scope, independent of the
/// caller's own request scope/cancellation. This is the outermost safety net only - it logs
/// anything that escapes the work delegate unhandled; callers that need to react to a failure
/// (e.g. moving a ticket to Blocked) must catch it themselves inside the delegate, since by the
/// time it reaches here there's no request left to report back to.
/// </summary>
public sealed class BackgroundTaskRunner(IServiceScopeFactory scopeFactory, ILogger<BackgroundTaskRunner> logger) : IBackgroundTaskRunner
{
    public void Run(Func<IServiceProvider, CancellationToken, Task> work)
    {
        // ExecutionContext.SuppressFlow() stops Task.Run from capturing the calling request's
        // ExecutionContext - without it, the AsyncLocal backing IHttpContextAccessor.HttpContext
        // flows into this "detached" task anyway, so Program.cs's ICurrentUserContext factory
        // sees a non-null HttpContext and wrongly resolves HttpContextCurrentUserContext instead
        // of SystemCurrentUserContext here. That HttpContext instance can then get pooled and
        // reused by an unrelated later request (ASP.NET Core pools DefaultHttpContext once
        // IHttpContextAccessor is registered) before this delegate's first await point returns,
        // so IProjectAccessGuard ends up checking against whichever request currently owns that
        // recycled object - not the user who actually triggered this run.
        using (ExecutionContext.SuppressFlow())
        {
            _ = Task.Run(async () =>
            {
                using var scope = scopeFactory.CreateScope();
                try
                {
                    await work(scope.ServiceProvider, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unhandled exception in a detached background task.");
                }
            });
        }
    }
}
