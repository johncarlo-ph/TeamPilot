using TeamPilot.Domain.Entities;

namespace TeamPilot.Application.Users;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default);

    Task AddAsync(User user, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Guid>> GetAssignedProjectIdsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the user's full set of project assignments (admin "assign projects" is a set
    /// operation, not incremental add/remove).
    /// </summary>
    Task SetAssignedProjectsAsync(Guid userId, IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default);
}
