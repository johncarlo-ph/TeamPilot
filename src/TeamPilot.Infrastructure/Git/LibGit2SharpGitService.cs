using LibGit2Sharp;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Git;

namespace TeamPilot.Infrastructure.Git;

/// <summary>
/// Git operations against a project's local sandbox clone via LibGit2Sharp. The caller resolves
/// <c>repositoryPath</c> from the owning Project - this service holds no per-project state.
/// Opens and disposes a <see cref="Repository"/> handle per call rather than holding one open
/// for the service's lifetime, so it can safely be registered at any lifetime. Operations that
/// talk to the remote (clone/push/fetch) wrap LibGit2Sharp failures as
/// <see cref="GitOperationException"/>.
/// </summary>
public class LibGit2SharpGitService(IOptions<GitOptions> options, IHostEnvironment environment) : IGitService
{
    private readonly GitOptions _options = options.Value;

    /// <summary>Retries the network-facing LibGit2Sharp calls (clone/push/fetch) on transient
    /// failures - LibGit2Sharp doesn't distinguish a transient connectivity blip from a permanent
    /// error (bad URL/token) via exception type, so a permanent failure just takes a few extra
    /// attempts (with backoff) before surfacing as the same <see cref="GitOperationException"/> it
    /// would have without this pipeline. Local-only operations (commit/diff/merge) are
    /// deliberately not wrapped: they aren't subject to network transience, and blindly retrying a
    /// file write/commit risks duplicating side effects instead of just re-attempting a request.</summary>
    private static readonly ResiliencePipeline RemoteOperationResilience = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<LibGit2SharpException>(),
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromSeconds(1),
            UseJitter = true,
        })
        .Build();

    public Task<string> CloneAsync(Guid projectId, string remoteUrl, string accessToken, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                var localPath = Path.Combine(ResolveSandboxRoot(), projectId.ToString());

                try
                {
                    var cloneOptions = new CloneOptions();
                    cloneOptions.FetchOptions.CredentialsProvider = (_, _, _) => BuildCredentials(accessToken);

                    RemoteOperationResilience.Execute(() =>
                    {
                        // A failed attempt can leave a partial clone on disk; clear it before each
                        // attempt (a no-op on the first, since localPath is freshly computed) so a
                        // retry doesn't fail with a "directory already exists" error that would
                        // mask the real one.
                        if (Directory.Exists(localPath))
                        {
                            Directory.Delete(localPath, recursive: true);
                        }

                        Repository.Clone(remoteUrl, localPath, cloneOptions);
                    });
                }
                catch (LibGit2SharpException ex)
                {
                    throw new GitOperationException($"Could not clone '{remoteUrl}'. Check the repository URL and access token.", ex);
                }

                return localPath;
            },
            cancellationToken);

    public Task PushAsync(string repositoryPath, string branchName, string accessToken, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                using var repo = OpenRepository(repositoryPath);
                var branch = GetExistingBranch(repo, branchName);
                var remote = GetOriginRemote(repo);
                var pushOptions = new PushOptions { CredentialsProvider = (_, _, _) => BuildCredentials(accessToken) };

                try
                {
                    RemoteOperationResilience.Execute(() => repo.Network.Push(remote, branch.CanonicalName, pushOptions));
                }
                catch (LibGit2SharpException ex)
                {
                    throw new GitOperationException($"Could not push branch '{branchName}' to the remote.", ex);
                }
            },
            cancellationToken);

    public Task FetchAsync(string repositoryPath, string accessToken, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                using var repo = OpenRepository(repositoryPath);
                var remote = GetOriginRemote(repo);
                var fetchOptions = new FetchOptions { CredentialsProvider = (_, _, _) => BuildCredentials(accessToken) };

                try
                {
                    var refSpecs = remote.FetchRefSpecs.Select(spec => spec.Specification).ToList();
                    RemoteOperationResilience.Execute(() => Commands.Fetch(repo, remote.Name, refSpecs, fetchOptions, null));
                }
                catch (LibGit2SharpException ex)
                {
                    throw new GitOperationException("Could not fetch from the remote.", ex);
                }
            },
            cancellationToken);

    public Task<bool> BranchExistsAsync(string repositoryPath, string branchName, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                using var repo = OpenRepository(repositoryPath);
                return repo.Branches[branchName] != null || repo.Branches[$"origin/{branchName}"] != null;
            },
            cancellationToken);

    public Task EnsureBranchAsync(string repositoryPath, string branchName, string baseBranchName, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                using var repo = OpenRepository(repositoryPath);
                GetOrCreateBranch(repo, branchName, baseBranchName);
            },
            cancellationToken);

    /// <summary>
    /// Resolves and validates every path up front (via <see cref="ResolveSandboxedPath"/>) before writing
    /// anything, so a bad path from a hallucinated file block can't leave a half-written sandbox on disk if
    /// a later entry fails validation.
    /// </summary>
    public Task<GitCommitResult> CommitFilesAsync(
        string repositoryPath,
        string branchName,
        IReadOnlyDictionary<string, string> fileContentsByRelativePath,
        string message,
        string authorName,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                using var repo = OpenRepository(repositoryPath);
                var branch = GetOrCreateBranch(repo, branchName);
                Commands.Checkout(repo, branch);

                var fullPathsByRelativePath = fileContentsByRelativePath.Keys.ToDictionary(
                    relativeFilePath => relativeFilePath,
                    relativeFilePath => ResolveSandboxedPath(repo.Info.WorkingDirectory, relativeFilePath));

                foreach (var (relativeFilePath, fileContent) in fileContentsByRelativePath)
                {
                    var fullPath = fullPathsByRelativePath[relativeFilePath];
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                    File.WriteAllText(fullPath, fileContent);
                    Commands.Stage(repo, relativeFilePath);
                }

                var signature = new Signature(authorName, _options.DefaultAuthorEmail, DateTimeOffset.UtcNow);
                var commit = repo.Commit(message, signature, signature);

                var diff = commit.Parents.Any()
                    ? repo.Diff.Compare<Patch>(commit.Parents.First().Tree, commit.Tree)
                    : repo.Diff.Compare<Patch>(null, commit.Tree);

                return new GitCommitResult(commit.Sha, diff.Content);
            },
            cancellationToken);

    public Task<GitDiffResult> GetDiffAsync(string repositoryPath, string sourceBranch, string targetBranch, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                using var repo = OpenRepository(repositoryPath);
                var source = GetExistingBranch(repo, sourceBranch);
                var target = GetExistingBranch(repo, targetBranch);

                var patch = repo.Diff.Compare<Patch>(target.Tip.Tree, source.Tip.Tree);
                var files = patch.Select(p => new GitFileDiff(p.Path, p.Patch)).ToList();

                return new GitDiffResult(sourceBranch, targetBranch, files);
            },
            cancellationToken);

    public Task<GitMergeConflictResult> DetectMergeConflictsAsync(string repositoryPath, string sourceBranch, string targetBranch, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                using var repo = OpenRepository(repositoryPath);
                var source = GetExistingBranch(repo, sourceBranch);
                var target = GetExistingBranch(repo, targetBranch);

                Commands.Checkout(repo, target);

                var merger = new Signature("TeamPilot", _options.DefaultAuthorEmail, DateTimeOffset.UtcNow);
                var mergeOptions = new MergeOptions { CommitOnSuccess = false, FailOnConflict = false };
                var mergeResult = repo.Merge(source.Tip, merger, mergeOptions);

                try
                {
                    if (mergeResult.Status != MergeStatus.Conflicts)
                    {
                        return new GitMergeConflictResult(false, Array.Empty<GitConflictingFile>());
                    }

                    var conflictingFiles = repo.Index.Conflicts
                        .Select(conflict =>
                        {
                            var path = conflict.Ours?.Path ?? conflict.Theirs?.Path ?? conflict.Ancestor?.Path ?? "unknown";
                            var fullPath = Path.Combine(repo.Info.WorkingDirectory, path);
                            var content = File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty;
                            return new GitConflictingFile(path, content);
                        })
                        .ToList();

                    return new GitMergeConflictResult(true, conflictingFiles);
                }
                finally
                {
                    // Never leave the sandbox repo mid-merge - this was a check, not a real merge.
                    repo.Reset(ResetMode.Hard, target.Tip);
                }
            },
            cancellationToken);

    /// <summary>
    /// Merges <paramref name="sourceBranch"/> into <paramref name="targetBranch"/> as a real
    /// two-parent merge commit, applying <paramref name="resolutions"/> to any conflicting file it
    /// covers. Mirrors <see cref="DetectMergeConflictsAsync"/>'s trial-merge mechanics (same
    /// <c>CommitOnSuccess: false</c> merge, same conflict enumeration) but, instead of always
    /// resetting, resolves what it can and finishes the merge with an explicit
    /// <c>repo.Commit(...)</c> - LibGit2Sharp picks up the merge-in-progress state
    /// (<c>repo.Merge</c> leaves it in place until something commits or resets it) and records the
    /// second parent automatically, exactly like completing a conflicted <c>git merge</c> by hand.
    /// </summary>
    public Task<GitMergeResolutionResult> MergeWithResolutionsAsync(
        string repositoryPath,
        string sourceBranch,
        string targetBranch,
        IReadOnlyDictionary<string, string> resolutions,
        string mergerName,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                using var repo = OpenRepository(repositoryPath);
                var source = GetExistingBranch(repo, sourceBranch);
                var target = GetExistingBranch(repo, targetBranch);

                Commands.Checkout(repo, target);

                var merger = new Signature(mergerName, _options.DefaultAuthorEmail, DateTimeOffset.UtcNow);
                var mergeOptions = new MergeOptions { CommitOnSuccess = false, FailOnConflict = false };
                var mergeResult = repo.Merge(source.Tip, merger, mergeOptions);
                var message = $"Merge branch '{sourceBranch}' into '{targetBranch}'";

                if (mergeResult.Status is MergeStatus.UpToDate or MergeStatus.FastForward)
                {
                    // Nothing left to commit - UpToDate means there was nothing to merge, and a
                    // fast-forward already moved the branch ref without needing a merge commit.
                    return new GitMergeResolutionResult(true, Array.Empty<string>());
                }

                if (mergeResult.Status != MergeStatus.Conflicts)
                {
                    // NonFastForward, no conflicts: the index already reflects a clean merge
                    // (CommitOnSuccess: false just means it's waiting to be committed).
                    repo.Commit(message, merger, merger);
                    return new GitMergeResolutionResult(true, Array.Empty<string>());
                }

                var unresolvedFilePaths = new List<string>();
                foreach (var conflict in repo.Index.Conflicts.ToList())
                {
                    var path = conflict.Ours?.Path ?? conflict.Theirs?.Path ?? conflict.Ancestor?.Path ?? "unknown";
                    if (resolutions.TryGetValue(path, out var resolvedContent))
                    {
                        var fullPath = Path.Combine(repo.Info.WorkingDirectory, path);
                        File.WriteAllText(fullPath, resolvedContent);
                        Commands.Stage(repo, path);
                    }
                    else
                    {
                        unresolvedFilePaths.Add(path);
                    }
                }

                if (unresolvedFilePaths.Count > 0)
                {
                    // Don't leave the shared sandbox mid-merge for another ticket's pipeline stage
                    // to trip over - abort exactly like DetectMergeConflictsAsync does.
                    repo.Reset(ResetMode.Hard, target.Tip);
                    return new GitMergeResolutionResult(false, unresolvedFilePaths);
                }

                repo.Commit(message, merger, merger);
                return new GitMergeResolutionResult(true, Array.Empty<string>());
            },
            cancellationToken);

    public Task DeleteBranchAsync(string repositoryPath, string branchName, string baseBranchName, string accessToken, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                using var repo = OpenRepository(repositoryPath);

                // Can't delete the currently checked-out branch - if the sandbox's HEAD is
                // sitting on it (e.g. left there by the last commit), switch to the base branch
                // first so the local delete below doesn't fail.
                if (string.Equals(repo.Head.FriendlyName, branchName, StringComparison.Ordinal))
                {
                    var baseBranch = repo.Branches[baseBranchName];
                    if (baseBranch is not null)
                    {
                        Commands.Checkout(repo, baseBranch);
                    }
                }

                var remote = GetOriginRemote(repo);
                var pushOptions = new PushOptions { CredentialsProvider = (_, _, _) => BuildCredentials(accessToken) };

                try
                {
                    // An empty source ref is the standard Git protocol convention for "delete
                    // this ref on the remote".
                    RemoteOperationResilience.Execute(() => repo.Network.Push(remote, $":refs/heads/{branchName}", pushOptions));
                }
                catch (LibGit2SharpException ex)
                {
                    throw new GitOperationException($"Could not delete branch '{branchName}' from the remote.", ex);
                }

                var localBranch = repo.Branches[branchName];
                if (localBranch is not null)
                {
                    repo.Branches.Remove(localBranch);
                }
            },
            cancellationToken);

    private const int MaxListEntries = 200;
    private const int MaxFileReadChars = 20_000;
    private const int MaxSnapshotFiles = 100;
    private const int MaxSnapshotTotalChars = 60_000;

    private static readonly string[] BlockedExtensions = [".pfx", ".pem", ".key", ".p12"];

    /// <summary>Directories never worth spending the snapshot's budget on - dependency/build
    /// output, not source the Coding stage would ever need to read or rewrite.</summary>
    private static readonly string[] SnapshotIgnoredDirectories = [".git", "node_modules", "bin", "obj", "dist", ".angular"];

    /// <summary>Extensions skipped outright rather than read as text - <c>File.ReadAllText</c>
    /// doesn't throw on binary content, it just returns mojibake, which would waste the
    /// snapshot's character budget on noise instead of throwing something worth catching.</summary>
    private static readonly string[] SnapshotBinaryExtensions =
    [
        ".png", ".jpg", ".jpeg", ".gif", ".ico", ".bmp", ".webp", ".svg",
        ".pdf", ".zip", ".dll", ".exe", ".woff", ".woff2", ".ttf", ".eot",
    ];

    public Task<IReadOnlyList<string>> ListFilesAsync(string repositoryPath, string? relativePath, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                using var repo = OpenRepository(repositoryPath);
                var targetPath = ResolveSandboxedPath(repo.Info.WorkingDirectory, relativePath ?? string.Empty);

                if (!Directory.Exists(targetPath))
                {
                    return (IReadOnlyList<string>)Array.Empty<string>();
                }

                var entries = new List<string>();

                foreach (var dir in Directory.EnumerateDirectories(targetPath))
                {
                    var name = Path.GetFileName(dir);
                    if (string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    entries.Add(name + "/");
                }

                foreach (var file in Directory.EnumerateFiles(targetPath))
                {
                    entries.Add(Path.GetFileName(file));
                }

                return (IReadOnlyList<string>)entries
                    .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
                    .Take(MaxListEntries)
                    .ToList();
            },
            cancellationToken);

    public Task<GitFileReadResult> ReadFileAsync(string repositoryPath, string relativeFilePath, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                if (IsBlockedPath(relativeFilePath))
                {
                    return new GitFileReadResult(false, null, false);
                }

                using var repo = OpenRepository(repositoryPath);
                var fullPath = ResolveSandboxedPath(repo.Info.WorkingDirectory, relativeFilePath);

                if (!File.Exists(fullPath))
                {
                    return new GitFileReadResult(false, null, false);
                }

                var content = SecretRedactor.Redact(File.ReadAllText(fullPath));
                var truncated = content.Length > MaxFileReadChars;

                return new GitFileReadResult(true, truncated ? content[..MaxFileReadChars] : content, truncated);
            },
            cancellationToken);

    /// <summary>
    /// Checks out <paramref name="branchName"/> and walks its working directory for a bounded, redacted
    /// text snapshot - stops adding files once <see cref="MaxSnapshotFiles"/> or
    /// <see cref="MaxSnapshotTotalChars"/> is hit, so a large repo degrades to a partial snapshot instead
    /// of an unbounded prompt. Skips the same blocked/secret-bearing paths as <see cref="ReadFileAsync"/>
    /// plus <see cref="SnapshotIgnoredDirectories"/> (dependency/build output never worth the budget).
    /// </summary>
    public Task<IReadOnlyDictionary<string, string>> GetRepositorySnapshotAsync(
        string repositoryPath, string branchName, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                using var repo = OpenRepository(repositoryPath);
                var branch = GetOrCreateBranch(repo, branchName);
                Commands.Checkout(repo, branch);

                var workingDirectory = repo.Info.WorkingDirectory;
                var snapshot = new Dictionary<string, string>();
                var totalChars = 0;

                foreach (var fullPath in EnumerateSnapshotFiles(workingDirectory))
                {
                    if (snapshot.Count >= MaxSnapshotFiles || totalChars >= MaxSnapshotTotalChars)
                    {
                        break;
                    }

                    var relativeFilePath = Path.GetRelativePath(workingDirectory, fullPath).Replace('\\', '/');
                    if (IsBlockedPath(relativeFilePath) || SnapshotBinaryExtensions.Contains(Path.GetExtension(fullPath), StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string content;
                    try
                    {
                        content = SecretRedactor.Redact(File.ReadAllText(fullPath));
                    }
                    catch (IOException)
                    {
                        // Unreadable for some other reason (locked, race with a concurrent write) -
                        // skip rather than fail the whole snapshot.
                        continue;
                    }

                    if (content.Length > MaxFileReadChars)
                    {
                        content = content[..MaxFileReadChars];
                    }

                    snapshot[relativeFilePath] = content;
                    totalChars += content.Length;
                }

                return (IReadOnlyDictionary<string, string>)snapshot;
            },
            cancellationToken);

    private static IEnumerable<string> EnumerateSnapshotFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            yield return file;
        }

        foreach (var subdirectory in Directory.EnumerateDirectories(directory).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(subdirectory);
            if (SnapshotIgnoredDirectories.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var file in EnumerateSnapshotFiles(subdirectory))
            {
                yield return file;
            }
        }
    }

    /// <summary>Resolves <paramref name="relativePath"/> against the repository's working
    /// directory and rejects it (via <see cref="GitOperationException"/>) if the resolved path
    /// would land outside that directory - the read-only file tools' only defense against a
    /// path-traversal attempt (e.g. <c>../../</c>) reaching outside the sandbox.</summary>
    private static string ResolveSandboxedPath(string workingDirectory, string relativePath)
    {
        var root = Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var resolved = Path.GetFullPath(Path.Combine(root, relativePath));

        if (!resolved.Equals(root, StringComparison.OrdinalIgnoreCase) &&
            !resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new GitOperationException($"Path '{relativePath}' is outside the repository sandbox.");
        }

        return resolved;
    }

    private static bool IsBlockedPath(string relativeFilePath)
    {
        var segments = relativeFilePath.Replace('\\', '/').TrimStart('/').Split('/');

        if (segments.Any(s => string.Equals(s, ".git", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var fileName = segments[^1];

        if (fileName.StartsWith(".env", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("id_rsa", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("id_ed25519", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return BlockedExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);
    }

    private string ResolveSandboxRoot() =>
        Path.IsPathRooted(_options.SandboxRoot)
            ? _options.SandboxRoot
            : Path.Combine(environment.ContentRootPath, _options.SandboxRoot);

    private static Remote GetOriginRemote(Repository repo) =>
        repo.Network.Remotes["origin"]
            ?? throw new InvalidOperationException("Repository has no 'origin' remote configured.");

    /// <summary>PAT-over-HTTPS convention accepted by GitHub and GitLab (token as username,
    /// blank password). Azure DevOps/Bitbucket may need the token in the password slot instead -
    /// this is the one place that would need to change to support those.</summary>
    private static Credentials BuildCredentials(string accessToken) =>
        new UsernamePasswordCredentials { Username = accessToken, Password = string.Empty };

    private static Repository OpenRepository(string repositoryPath)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath) || !Repository.IsValid(repositoryPath))
        {
            throw new InvalidOperationException(
                $"'{repositoryPath}' is not a valid Git repository. Clone or initialize it before using Git-backed features.");
        }

        return new Repository(repositoryPath);
    }

    private static Branch GetOrCreateBranch(Repository repo, string branchName, string? baseBranchName = null)
    {
        var existing = repo.Branches[branchName];
        if (existing != null)
        {
            return existing;
        }

        if (baseBranchName is null)
        {
            return repo.CreateBranch(branchName);
        }

        var baseBranch = repo.Branches[baseBranchName]
            ?? throw new InvalidOperationException($"Base branch '{baseBranchName}' does not exist in the repository.");

        return repo.CreateBranch(branchName, baseBranch.Tip);
    }

    private static Branch GetExistingBranch(Repository repo, string branchName) =>
        repo.Branches[branchName]
            ?? throw new InvalidOperationException($"Branch '{branchName}' does not exist in the repository.");
}
