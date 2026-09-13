using System.Collections.Concurrent;

namespace TeamPilot.Application.Git;

/// <summary>
/// Serializes work against a project's local Git sandbox, keyed by repository path.
/// <see cref="IGitService"/>'s one implementation opens and disposes a fresh LibGit2Sharp
/// <c>Repository</c> handle per call with no coordination of its own between calls, and every
/// ticket in a project shares that same one local clone (<c>Project.RepositoryPath</c>) - so two
/// concurrent operations against it (e.g. a Coding stage's commit/push racing a ticket
/// cancellation's branch delete, or two tickets' pipelines committing at once) could otherwise
/// corrupt the working directory or race on the same remote ref. Every call site that mutates or
/// checks out a project's sandbox acquires this first - see docs/application.md.
///
/// Static and keyed by path rather than injected, matching
/// <c>LibGit2SharpGitService.RemoteOperationResilience</c>'s own static-cross-cutting-helper
/// style - there is exactly one process talking to any given sandbox clone, so a per-process
/// dictionary is correct here.
/// </summary>
public static class GitRepositoryLock
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> GatesByPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Acquires the lock for <paramref name="repositoryPath"/>, waiting if another
    /// operation currently holds it. Dispose the result (e.g. via <c>await using</c>) to
    /// release it - holding it across every git call (and any status re-check that must observe
    /// their result) in one logical operation is what makes that operation atomic relative to
    /// every other one against the same repository.</summary>
    public static async Task<IAsyncDisposable> AcquireAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var gate = GatesByPath.GetOrAdd(NormalizePath(repositoryPath), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    // Trimmed, not resolved via Path.GetFullPath - repositoryPath is already the absolute path
    // IGitService.CloneAsync returned and Project persists, so there's no relative-vs-absolute
    // ambiguity to resolve here, and staying string-only means this never throws on the
    // malformed/empty paths a unit test's in-memory Project (no real clone) can carry.
    private static string NormalizePath(string repositoryPath) =>
        repositoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private sealed class Releaser(SemaphoreSlim gate) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
