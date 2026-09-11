using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Common.Interfaces;

/// <summary>
/// The authenticated caller for the current request. Implemented in the Presentation layer
/// (it wraps <c>HttpContext</c>), declared here so Application services can depend on it
/// without knowing about ASP.NET Core.
/// </summary>
public interface ICurrentUserContext
{
    bool IsAuthenticated { get; }

    Guid UserId { get; }

    string? Name { get; }

    string? Email { get; }

    IReadOnlyCollection<UserRole> Roles { get; }

    bool IsInRole(UserRole role);
}
