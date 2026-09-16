namespace TeamPilot.Application.Common.Exceptions;

/// <summary>
/// Thrown when approving a ticket whose merge finds that one or more already-resolved conflicts
/// were prepared against a version of the base branch that has since moved further in that same
/// region - see <c>IGitService.MergeWithResolutionsAsync</c>'s <c>StaleFilePaths</c> and
/// <c>Conflict.BaseTipSha</c>/<c>MarkStale</c>. Distinct from <see cref="UnresolvedConflictsException"/>
/// (which covers a conflicting file with no resolution at all): here a resolution exists but can
/// no longer be trusted, so it's reset to <c>Detected</c> rather than applied blindly, and the
/// reviewer is asked to re-resolve it.
/// </summary>
public sealed class StaleConflictResolutionException(IReadOnlyList<string> filePaths)
    : Exception($"The base branch has moved since the following file(s) were resolved - please re-resolve: {string.Join(", ", filePaths)}")
{
    public IReadOnlyList<string> FilePaths { get; } = filePaths;
}
