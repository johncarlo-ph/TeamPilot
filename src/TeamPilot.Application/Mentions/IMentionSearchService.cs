using TeamPilot.Application.Mentions.Dtos;

namespace TeamPilot.Application.Mentions;

/// <summary>
/// Backs the "@" mention-autocomplete dropdown a user gets while writing a ticket's description
/// (see <see cref="Tickets.Mentions.MentionParser"/>) - searches tickets/projects across every
/// project the caller has access to (not just the ticket being created), reusing the same
/// access-scoping <see cref="Projects.ProjectService.ListAsync"/> already applies.
/// </summary>
public interface IMentionSearchService
{
    Task<IReadOnlyList<TicketMentionResultDto>> SearchTicketsAsync(string query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectMentionResultDto>> SearchProjectsAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unlike <see cref="SearchTicketsAsync"/>/<see cref="SearchProjectsAsync"/> (which span every
    /// project the caller can access), file search is scoped to one <paramref name="projectId"/>
    /// at a time - the frontend calls this once per project it's allowed to file-reference (the
    /// ticket's own, plus any already directly "@project"-mentioned - see docs/frontend.md) and
    /// merges the results itself. Returns empty (not an error) for a project that isn't
    /// <see cref="Domain.Enums.ProjectStatus.Ready"/> yet, same as a too-short query.
    /// </summary>
    Task<IReadOnlyList<FileMentionResultDto>> SearchFilesAsync(Guid projectId, string query, CancellationToken cancellationToken = default);
}
