using TeamPilot.Domain.Common;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Domain.Entities;

/// <summary>
/// An authenticated principal, identified by their verified external (Google/Microsoft)
/// email and display name so admins can recognize who a user is. New users start with zero
/// roles (secure by default); an Admin must grant access.
/// </summary>
public class User : Entity
{
    private readonly List<UserRole> _roles = [];

    public string Name { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public UserStatus Status { get; private set; }

    public IReadOnlyCollection<UserRole> Roles => _roles.AsReadOnly();

    private User()
    {
    }

    public static User Create(string name, string email)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required.", nameof(email));
        }

        return new User
        {
            Name = name.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            Status = UserStatus.Active,
        };
    }

    public bool HasRole(UserRole role) => _roles.Contains(role);

    /// <summary>
    /// Keeps the display name in sync with the identity provider - called on every login,
    /// not just at creation, in case the user renamed themselves at Google/Microsoft.
    /// </summary>
    public void UpdateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == Name)
        {
            return;
        }

        Name = name.Trim();
        MarkUpdated();
    }

    /// <summary>
    /// Replaces the user's full role set (the admin "assign roles" operation is a set,
    /// not an incremental add/remove).
    /// </summary>
    public void SetRoles(IEnumerable<UserRole> roles)
    {
        _roles.Clear();
        _roles.AddRange(roles.Distinct());
        MarkUpdated();
    }

    public void Disable()
    {
        Status = UserStatus.Disabled;
        MarkUpdated();
    }

    public void Enable()
    {
        Status = UserStatus.Active;
        MarkUpdated();
    }
}
