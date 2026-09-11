using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Users;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Common;

public sealed class ProjectAccessGuard(ICurrentUserContext currentUser, IUserRepository userRepository) : IProjectAccessGuard
{
    public async Task EnsureAccessAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        if (currentUser.IsInRole(UserRole.Admin))
        {
            return;
        }

        var assignedProjectIds = await userRepository.GetAssignedProjectIdsAsync(currentUser.UserId, cancellationToken);

        if (!assignedProjectIds.Contains(projectId))
        {
            throw new ForbiddenException($"You do not have access to project '{projectId}'.");
        }
    }
}
