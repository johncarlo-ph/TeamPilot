namespace TeamPilot.Application.Git;

/// <summary>
/// Abstraction over Git operations against a project's local sandbox clone of its remote
/// repository. The caller resolves <c>repositoryPath</c> from the owning <c>Project</c> - this
/// service holds no per-project state. The one concrete implementation (Infrastructure) uses
/// LibGit2Sharp. Local-only operations (commit/diff/detect-conflicts/merge) take no credentials;
/// operations that actually talk to the remote (clone/push/fetch) do, and throw
/// <see cref="TeamPilot.Application.Common.Exceptions.GitOperationException"/> on failure.
/// </summary>
public interface IGitService
{
    /// <summary>Clones <paramref name="remoteUrl"/> into a new server-managed sandbox folder
    /// for the given project and returns the resulting local path. When given,
    /// <paramref name="progress"/> is reported transfer/checkout progress throughout - see
    /// <see cref="GitCloneProgress"/> - so a caller can surface a live progress bar; it's never
    /// required for the clone to succeed.</summary>
    Task<string> CloneAsync(Guid projectId, string remoteUrl, string accessToken, IProgress<GitCloneProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Returns whether <paramref name="branchName"/> exists as a head on the remote
    /// itself, without cloning it locally - used to validate a project's base branch before its
    /// sandbox clone even exists (see <c>ProjectService.CreateAsync</c>). Throws
    /// <see cref="TeamPilot.Application.Common.Exceptions.GitOperationException"/> if the remote
    /// can't be reached at all (bad URL/token), same as <see cref="CloneAsync"/>.</summary>
    Task<bool> RemoteBranchExistsAsync(string remoteUrl, string accessToken, string branchName, CancellationToken cancellationToken = default);

    /// <summary>Pushes a single branch to the sandbox's <c>origin</c> remote.</summary>
    Task PushAsync(string repositoryPath, string branchName, string accessToken, CancellationToken cancellationToken = default);

    /// <summary>Fetches the latest refs from the sandbox's <c>origin</c> remote.</summary>
    Task FetchAsync(string repositoryPath, string accessToken, CancellationToken cancellationToken = default);

    /// <summary>Returns whether <paramref name="branchName"/> already exists, either as a local
    /// branch or as a remote-tracking branch from <c>origin</c> (reflects the state as of the
    /// last <see cref="FetchAsync"/>).</summary>
    Task<bool> BranchExistsAsync(string repositoryPath, string branchName, CancellationToken cancellationToken = default);

    /// <summary>Creates <paramref name="branchName"/> from the tip of
    /// <paramref name="baseBranchName"/> if it doesn't already exist locally.</summary>
    Task EnsureBranchAsync(string repositoryPath, string branchName, string baseBranchName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes every entry of <paramref name="fileContentsByRelativePath"/> to <paramref name="branchName"/>'s
    /// working directory and commits them all together as one commit (so <c>Commit.DiffContent</c> is a real
    /// multi-file <c>git diff</c>-style patch when more than one file changed).
    /// </summary>
    Task<GitCommitResult> CommitFilesAsync(
        string repositoryPath,
        string branchName,
        IReadOnlyDictionary<string, string> fileContentsByRelativePath,
        string message,
        string authorName,
        CancellationToken cancellationToken = default);

    Task<GitDiffResult> GetDiffAsync(string repositoryPath, string sourceBranch, string targetBranch, CancellationToken cancellationToken = default);

    Task<GitMergeConflictResult> DetectMergeConflictsAsync(string repositoryPath, string sourceBranch, string targetBranch, CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges <paramref name="sourceBranch"/> into <paramref name="targetBranch"/> and commits the
    /// result - a real two-parent merge commit when the merge isn't a fast-forward. If Git reports
    /// conflicts, <paramref name="resolutions"/> (file path -&gt; the resolved content plus the base
    /// branch tip it was prepared against) is applied to whichever conflicting files it covers -
    /// unless that file's <see cref="GitConflictResolution.BaseTipSha"/> no longer matches the
    /// base branch's live tip, in which case it's treated as stale rather than applied (see
    /// <paramref name="resolutions"/> vs. <see cref="GitMergeResolutionResult.StaleFilePaths"/>).
    /// Any conflicting file <paramref name="resolutions"/> doesn't cover at all means the merge
    /// can't complete. Either way the attempt is aborted (nothing is committed or left in a
    /// mid-merge state) and the returned result lists exactly which files are unresolved vs.
    /// stale. This is a live check against the real merge, not a trust of whatever the caller
    /// already believes is resolved - see docs/application.md. <paramref name="mergerName"/> is
    /// used as the merge commit's author/committer signature and is also embedded in the commit
    /// message itself (e.g. "... (approved by {mergerName})"), so the Git history records who
    /// approved the merge.
    /// </summary>
    Task<GitMergeResolutionResult> MergeWithResolutionsAsync(
        string repositoryPath,
        string sourceBranch,
        string targetBranch,
        IReadOnlyDictionary<string, GitConflictResolution> resolutions,
        string mergerName,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes <paramref name="branchName"/> from both the remote and the local
    /// sandbox. <paramref name="baseBranchName"/> is checked out first if the sandbox's HEAD
    /// happens to be sitting on the branch being deleted.</summary>
    Task DeleteBranchAsync(string repositoryPath, string branchName, string baseBranchName, string accessToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read-only, recursive listing of every file under <paramref name="relativePath"/> (or the
    /// whole repository when <see langword="null"/>/empty), as paths relative to
    /// <paramref name="relativePath"/> itself - so a caller can see a whole subtree in one call
    /// instead of paying one call per directory level. Skips dependency/build-output directories
    /// (<c>.git</c>, <c>node_modules</c>, <c>bin</c>, <c>obj</c>, <c>dist</c>, <c>.angular</c>) and
    /// stops at a bounded entry count, appending a note to retry with a more specific path if the
    /// result was truncated. Backs the Live Agent chat's and the Research/Design/Coding pipeline
    /// stages' sandboxed file-browsing tool (see
    /// <see cref="TeamPilot.Application.Git.GitReadOnlyTools"/>) - never writes, and rejects any
    /// path that would resolve outside the repository's working directory. When
    /// <paramref name="branchName"/> is given, that branch is checked out first so the listing
    /// reflects that branch's tip rather than whatever happens to already be checked out.
    /// </summary>
    Task<IReadOnlyList<string>> ListFilesAsync(string repositoryPath, string? relativePath, string? branchName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read-only read of one file's text content from the repository's working directory, backing
    /// the Live Agent chat's and the Research/Design/Coding pipeline stages' file-reading tool (see
    /// <see cref="TeamPilot.Application.Git.GitReadOnlyTools"/>). Refuses well-known secret-bearing
    /// paths outright and best-effort redacts common secret shapes from what it does return (see
    /// <c>SecretRedactor</c>) - not a guarantee, just a defense in depth. Rejects any path that
    /// would resolve outside the repository's working directory. When <paramref name="branchName"/>
    /// is given, that branch is checked out first so the read reflects that branch's tip.
    /// </summary>
    Task<GitFileReadResult> ReadFileAsync(string repositoryPath, string relativeFilePath, string? branchName = null, CancellationToken cancellationToken = default);
}

public sealed record GitCommitResult(string CommitHash, string DiffContent);

public sealed record GitFileDiff(string FilePath, string Patch);

public sealed record GitDiffResult(string SourceBranch, string TargetBranch, IReadOnlyList<GitFileDiff> Files);

public sealed record GitConflictingFile(string FilePath, string ConflictContent);

/// <param name="BaseTipSha">The base branch's (<paramref name="targetBranch"/>'s) commit SHA at
/// the moment conflicts were detected - stamped onto each new <c>Conflict</c> so a later merge
/// attempt can tell whether the base branch has moved since.</param>
public sealed record GitMergeConflictResult(bool HasConflicts, IReadOnlyList<GitConflictingFile> ConflictingFiles, string BaseTipSha);

/// <summary>A resolved conflicting file's content, plus the base branch tip it was prepared
/// against (see <c>Domain.Entities.Conflict.BaseTipSha</c>) - <see cref="IGitService.MergeWithResolutionsAsync"/>
/// re-checks <paramref name="BaseTipSha"/> against the live tip before applying
/// <paramref name="ResolvedContent"/>.</summary>
public sealed record GitConflictResolution(string ResolvedContent, string? BaseTipSha);

/// <summary><paramref name="UnresolvedFilePaths"/> and <paramref name="StaleFilePaths"/> are both
/// empty whenever <paramref name="Success"/> is <see langword="true"/>. <paramref name="UnresolvedFilePaths"/>
/// names a conflicting file with no resolution available at all; <paramref name="StaleFilePaths"/>
/// names one with a resolution that wasn't applied because its <see cref="GitConflictResolution.BaseTipSha"/>
/// no longer matches the base branch's live tip.</summary>
public sealed record GitMergeResolutionResult(bool Success, IReadOnlyList<string> UnresolvedFilePaths, IReadOnlyList<string> StaleFilePaths);

/// <summary>Clone progress, reported throughout both the object-transfer phase
/// (<paramref name="ReceivedObjects"/>/<paramref name="TotalObjects"/>/<paramref name="ReceivedBytes"/>,
/// from LibGit2Sharp's <c>OnTransferProgress</c>) and the working-directory checkout phase that
/// follows it (<paramref name="CheckoutCompletedSteps"/>/<paramref name="CheckoutTotalSteps"/>,
/// from <c>OnCheckoutProgress</c>). <paramref name="TotalObjects"/> and
/// <paramref name="CheckoutTotalSteps"/> are 0 until Git has negotiated enough with the remote to
/// know them - a caller should treat that as "indeterminate" rather than "0% of 0".</summary>
public sealed record GitCloneProgress(
    long ReceivedObjects,
    long TotalObjects,
    long ReceivedBytes,
    int CheckoutCompletedSteps,
    int CheckoutTotalSteps);

/// <summary><paramref name="Truncated"/> is <see langword="true"/> when <paramref name="Content"/>
/// was cut short of the file's actual length to bound how much text reaches an LLM prompt.</summary>
public sealed record GitFileReadResult(bool Found, string? Content, bool Truncated);
