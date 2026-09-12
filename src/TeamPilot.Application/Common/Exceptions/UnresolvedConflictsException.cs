namespace TeamPilot.Application.Common.Exceptions;

/// <summary>
/// Thrown when approving a ticket whose branch has one or more merge conflicts that haven't
/// been resolved yet - a use-case constraint (it depends on the ticket's <c>Conflict</c> child
/// records, which <see cref="Domain.Entities.Ticket"/> doesn't strictly own - see docs/domain.md
/// on loose aggregate boundaries), not something the entity could enforce on its own. Thrown both
/// as an early check against the ticket's own <c>Conflict</c> records, and again if
/// <c>IGitService.MergeWithResolutionsAsync</c>'s live merge attempt finds a conflicting file with
/// no resolution available (e.g. one that appeared because the base branch moved further since it
/// was last checked) - see docs/application.md.
/// </summary>
public sealed class UnresolvedConflictsException(IReadOnlyList<string> filePaths)
    : Exception($"Cannot approve this ticket while the following file(s) have unresolved conflicts: {string.Join(", ", filePaths)}")
{
    public IReadOnlyList<string> FilePaths { get; } = filePaths;
}
