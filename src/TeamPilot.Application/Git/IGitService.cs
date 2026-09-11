namespace TeamPilot.Application.Git;

/// <summary>
/// Abstraction over Git operations against a project's local sandbox repository. The caller
/// resolves <c>repositoryPath</c> from the owning <c>Project</c> - this service holds no
/// per-project state. The one concrete implementation (Infrastructure) uses LibGit2Sharp.
/// </summary>
public interface IGitService
{
    Task EnsureBranchAsync(string repositoryPath, string branchName, CancellationToken cancellationToken = default);

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
