using TeamPilot.Domain.Entities;

namespace TeamPilot.Application.Projects;

public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Project project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lean, read-only, name-matching search for the "@" mention-autocomplete dropdown (see
    /// <c>Mentions.IMentionSearchService</c>) - excludes removed projects, capped at
    /// <paramref name="maxResults"/>. <paramref name="allowedProjectIds"/> restricts results to
    /// those projects; <see langword="null"/> means unrestricted (an Admin caller).
    /// </summary>
    Task<IReadOnlyList<Project>> SearchAsync(string query, IReadOnlyCollection<Guid>? allowedProjectIds, int maxResults, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lean, read-only bulk lookup by id - used by <c>TicketService.ValidateMentionsAsync</c>,
    /// <c>Mentions.MentionSearchService</c>, and <c>Orchestration.OrchestrationService</c> to
    /// resolve "@project" mentions parsed out of a ticket's description.
    /// </summary>
    Task<IReadOnlyList<Project>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
