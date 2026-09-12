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
    /// for the given project and returns the resulting local path.</summary>
    Task<string> CloneAsync(Guid projectId, string remoteUrl, string accessToken, CancellationToken cancellationToken = default);

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

    Task<GitCommitResult> CommitFileAsync(
        string repositoryPath,
        string branchName,
        string relativeFilePath,
        string fileContent,
        string message,
        string authorName,
        CancellationToken cancellationToken = default);

    Task<GitDiffResult> GetDiffAsync(string repositoryPath, string sourceBranch, string targetBranch, CancellationToken cancellationToken = default);

    Task<GitMergeConflictResult> DetectMergeConflictsAsync(string repositoryPath, string sourceBranch, string targetBranch, CancellationToken cancellationToken = default);

    Task MergeBranchAsync(string repositoryPath, string sourceBranch, string targetBranch, string mergerName, CancellationToken cancellationToken = default);

    /// <summary>Deletes <paramref name="branchName"/> from both the remote and the local
    /// sandbox. <paramref name="baseBranchName"/> is checked out first if the sandbox's HEAD
    /// happens to be sitting on the branch being deleted.</summary>
    Task DeleteBranchAsync(string repositoryPath, string branchName, string baseBranchName, string accessToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read-only, non-recursive listing of the files and folders directly inside
    /// <paramref name="relativePath"/> (or the repository root when <see langword="null"/>/empty).
    /// Folder entries are suffixed with <c>/</c>. Used only by the Live Agent chat's sandboxed
    /// file-browsing tool - never writes, and rejects any path that would resolve outside the
    /// repository's working directory.
    /// </summary>
    Task<IReadOnlyList<string>> ListFilesAsync(string repositoryPath, string? relativePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read-only read of one file's text content from the repository's working directory, for
    /// the Live Agent chat's file-reading tool. Refuses well-known secret-bearing paths outright
    /// and best-effort redacts common secret shapes from what it does return (see
    /// <c>SecretRedactor</c>) - not a guarantee, just a defense in depth. Rejects any path that
    /// would resolve outside the repository's working directory.
    /// </summary>
    Task<GitFileReadResult> ReadFileAsync(string repositoryPath, string relativeFilePath, CancellationToken cancellationToken = default);
}

public sealed record GitCommitResult(string CommitHash, string DiffContent);

public sealed record GitFileDiff(string FilePath, string Patch);

public sealed record GitDiffResult(string SourceBranch, string TargetBranch, IReadOnlyList<GitFileDiff> Files);

public sealed record GitConflictingFile(string FilePath, string ConflictContent);

public sealed record GitMergeConflictResult(bool HasConflicts, IReadOnlyList<GitConflictingFile> ConflictingFiles);

/// <summary><paramref name="Truncated"/> is <see langword="true"/> when <paramref name="Content"/>
/// was cut short of the file's actual length to bound how much text reaches an LLM prompt.</summary>
public sealed record GitFileReadResult(bool Found, string? Content, bool Truncated);
