using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Git;
using TeamPilot.Application.Mentions.Dtos;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Tickets;
using TeamPilot.Application.Users;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Mentions;

public sealed class MentionSearchService(
    ITicketRepository ticketRepository,
    IProjectRepository projectRepository,
    IGitService gitService,
    IProjectAccessGuard projectAccessGuard,
    ICurrentUserContext currentUser,
    IUserRepository userRepository) : IMentionSearchService
{
    private const int MinQueryLength = 2;
    private const int MaxResults = 10;

    public async Task<IReadOnlyList<TicketMentionResultDto>> SearchTicketsAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < MinQueryLength)
        {
            return [];
        }

        var allowedProjectIds = await GetAllowedProjectIdsAsync(cancellationToken);
        var tickets = await ticketRepository.SearchAsync(query.Trim(), allowedProjectIds, MaxResults, cancellationToken);
        var projectNamesById = await GetProjectNamesByIdAsync(tickets.Select(t => t.ProjectId).Distinct(), cancellationToken);

        return tickets
            .Select(t => new TicketMentionResultDto(t.Id, t.Title, t.ProjectId, projectNamesById.GetValueOrDefault(t.ProjectId, string.Empty)))
            .ToList();
    }

    public async Task<IReadOnlyList<ProjectMentionResultDto>> SearchProjectsAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < MinQueryLength)
        {
            return [];
        }

        var allowedProjectIds = await GetAllowedProjectIdsAsync(cancellationToken);
        var projects = await projectRepository.SearchAsync(query.Trim(), allowedProjectIds, MaxResults, cancellationToken);

        return projects.Select(p => new ProjectMentionResultDto(p.Id, p.Name)).ToList();
    }

    public async Task<IReadOnlyList<FileMentionResultDto>> SearchFilesAsync(Guid projectId, string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < MinQueryLength)
        {
            return [];
        }

        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);

        var project = await projectRepository.GetByIdAsync(projectId, cancellationToken);
        if (project is null || project.Status != ProjectStatus.Ready)
        {
            return [];
        }

        var paths = await gitService.SearchFilesAsync(project.RepositoryPath, query.Trim(), branchName: null, MaxResults, cancellationToken);
        return paths.Select(p => new FileMentionResultDto(p)).ToList();
    }

    /// <summary>
    /// <see langword="null"/> means unrestricted (an Admin caller) - matches
    /// <see cref="ProjectService.ListAsync"/>'s own Admin-bypass check, kept here rather than in
    /// either repository so neither one needs to know about roles.
    /// </summary>
    private async Task<IReadOnlyCollection<Guid>?> GetAllowedProjectIdsAsync(CancellationToken cancellationToken) =>
        currentUser.IsInRole(UserRole.Admin)
            ? null
            : await userRepository.GetAssignedProjectIdsAsync(currentUser.UserId, cancellationToken);

    private async Task<IReadOnlyDictionary<Guid, string>> GetProjectNamesByIdAsync(IEnumerable<Guid> projectIds, CancellationToken cancellationToken)
    {
        var ids = projectIds.ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var projects = await projectRepository.GetByIdsAsync(ids, cancellationToken);
        return projects.ToDictionary(p => p.Id, p => p.Name);
    }
}
