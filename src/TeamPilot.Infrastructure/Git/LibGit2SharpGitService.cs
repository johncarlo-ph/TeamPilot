using LibGit2Sharp;
using Microsoft.Extensions.Options;
using TeamPilot.Application.Git;

namespace TeamPilot.Infrastructure.Git;

/// <summary>
/// Git operations against a project's local sandbox repository via LibGit2Sharp. The caller
/// resolves <c>repositoryPath</c> from the owning Project - this service holds no per-project
/// state. Opens and disposes a <see cref="Repository"/> handle per call rather than holding
/// one open for the service's lifetime, so it can safely be registered at any lifetime.
/// </summary>
public class LibGit2SharpGitService(IOptions<GitOptions> options) : IGitService
{
    private readonly GitOptions _options = options.Value;

    public Task EnsureBranchAsync(string repositoryPath, string branchName, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                using var repo = OpenRepository(repositoryPath);
                GetOrCreateBranch(repo, branchName);
            },
            cancellationToken);

    public Task<GitCommitResult> CommitFileAsync(
        string repositoryPath,
        string branchName,
        string relativeFilePath,
        string fileContent,
        string message,
        string authorName,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                using var repo = OpenRepository(repositoryPath);
                var branch = GetOrCreateBranch(repo, branchName);
                Commands.Checkout(repo, branch);

                var fullPath = Path.Combine(repo.Info.WorkingDirectory, relativeFilePath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllText(fullPath, fileContent);

                Commands.Stage(repo, relativeFilePath);

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

    public Task MergeBranchAsync(string repositoryPath, string sourceBranch, string targetBranch, string mergerName, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                using var repo = OpenRepository(repositoryPath);
                var source = GetExistingBranch(repo, sourceBranch);
                var target = GetExistingBranch(repo, targetBranch);

                Commands.Checkout(repo, target);

                var merger = new Signature(mergerName, _options.DefaultAuthorEmail, DateTimeOffset.UtcNow);
                var mergeResult = repo.Merge(source.Tip, merger, new MergeOptions { CommitOnSuccess = true });

                if (mergeResult.Status == MergeStatus.Conflicts)
                {
                    repo.Reset(ResetMode.Hard, target.Tip);
                    throw new InvalidOperationException(
                        $"Merging '{sourceBranch}' into '{targetBranch}' resulted in conflicts. Resolve conflicts before approving.");
                }
            },
            cancellationToken);

    private static Repository OpenRepository(string repositoryPath)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath) || !Repository.IsValid(repositoryPath))
        {
            throw new InvalidOperationException(
                $"'{repositoryPath}' is not a valid Git repository. Initialize it (git init) with at least one commit before using Git-backed features.");
        }

        return new Repository(repositoryPath);
    }

    private static Branch GetOrCreateBranch(Repository repo, string branchName) =>
        repo.Branches[branchName] ?? repo.CreateBranch(branchName);

    private static Branch GetExistingBranch(Repository repo, string branchName) =>
        repo.Branches[branchName]
            ?? throw new InvalidOperationException($"Branch '{branchName}' does not exist in the repository.");
}
