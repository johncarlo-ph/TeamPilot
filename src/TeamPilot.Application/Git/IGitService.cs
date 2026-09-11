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
}

public sealed record GitCommitResult(string CommitHash, string DiffContent);

public sealed record GitFileDiff(string FilePath, string Patch);

public sealed record GitDiffResult(string SourceBranch, string TargetBranch, IReadOnlyList<GitFileDiff> Files);

public sealed record GitConflictingFile(string FilePath, string ConflictContent);

public sealed record GitMergeConflictResult(bool HasConflicts, IReadOnlyList<GitConflictingFile> ConflictingFiles);
