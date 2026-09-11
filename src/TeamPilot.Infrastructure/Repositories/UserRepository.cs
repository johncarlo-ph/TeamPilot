using Microsoft.EntityFrameworkCore;
using TeamPilot.Application.Users;
using TeamPilot.Domain.Entities;
using TeamPilot.Infrastructure.Persistence;

namespace TeamPilot.Infrastructure.Repositories;

public class UserRepository(TeamPilotDbContext dbContext) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        dbContext.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Users.AsNoTracking().OrderBy(u => u.CreatedAtUtc).ToListAsync(cancellationToken);

    public async Task AddAsync(User user, CancellationToken cancellationToken = default) =>
        await dbContext.Users.AddAsync(user, cancellationToken);

    public async Task<IReadOnlyCollection<Guid>> GetAssignedProjectIdsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await dbContext.UserProjectAssignments
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .Select(a => a.ProjectId)
            .ToListAsync(cancellationToken);

    public async Task SetAssignedProjectsAsync(Guid userId, IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.UserProjectAssignments
            .Where(a => a.UserId == userId)
            .ToListAsync(cancellationToken);

        dbContext.UserProjectAssignments.RemoveRange(existing);

        foreach (var projectId in projectIds.Distinct())
        {
            await dbContext.UserProjectAssignments.AddAsync(UserProjectAssignment.Create(userId, projectId), cancellationToken);
        }
    }
}
