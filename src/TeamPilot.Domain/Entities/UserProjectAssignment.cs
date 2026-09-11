using TeamPilot.Domain.Common;

namespace TeamPilot.Domain.Entities;

/// <summary>
/// Grants a user access to a project's ticket board. Managed with "set" semantics by the
/// repository (replace the full assignment set) rather than through an aggregate's own
/// collection, so <see cref="Create"/> is public.
/// </summary>
public class UserProjectAssignment : Entity
{
    public Guid UserId { get; private set; }

    public Guid ProjectId { get; private set; }

    public DateTime AssignedAtUtc { get; private set; }

    private UserProjectAssignment()
    {
    }

    public static UserProjectAssignment Create(Guid userId, Guid projectId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project id is required.", nameof(projectId));
        }

        return new UserProjectAssignment
        {
            UserId = userId,
            ProjectId = projectId,
            AssignedAtUtc = DateTime.UtcNow,
        };
    }
}
