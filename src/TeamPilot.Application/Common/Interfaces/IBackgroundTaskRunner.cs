namespace TeamPilot.Application.Common.Interfaces;

/// <summary>
/// Runs a unit of work detached from the caller's own request, in its own DI scope, so a caller
/// can return a response immediately instead of blocking on long-running work (e.g. re-running
/// the agent pipeline). The delegate must resolve every dependency it needs from the
/// <see cref="IServiceProvider"/> it's given - never close over services injected into the
/// caller, since the caller's own scope (and its scoped <c>DbContext</c>) is disposed once the
/// caller returns, before the detached work runs.
/// </summary>
public interface IBackgroundTaskRunner
{
    void Run(Func<IServiceProvider, CancellationToken, Task> work);
}
