using System.Security.Claims;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Domain.Enums;

namespace TeamPilot.API.Auth;

/// <summary>
/// Reads the authenticated caller off <c>HttpContext.User</c>. Relies on
/// <c>JwtBearerOptions.MapInboundClaims = false</c> (set in Program.cs) so claim types stay
/// exactly as <see cref="TeamPilot.Infrastructure.Auth.AccessTokenGenerator"/> issued them.
/// </summary>
public class HttpContextCurrentUserContext(IHttpContextAccessor httpContextAccessor) : ICurrentUserContext
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid UserId
    {
        get
        {
            var value = Principal?.FindFirst("sub")?.Value;
            return Guid.TryParse(value, out var userId) ? userId : Guid.Empty;
        }
    }

    public string? Name => Principal?.FindFirst("name")?.Value;

    public string? Email => Principal?.FindFirst("email")?.Value;

    public IReadOnlyCollection<UserRole> Roles =>
        Principal?.FindAll(ClaimTypes.Role)
            .Select(claim => Enum.TryParse<UserRole>(claim.Value, out var role) ? role : (UserRole?)null)
            .Where(role => role.HasValue)
            .Select(role => role!.Value)
            .ToList()
        ?? [];

    public bool IsInRole(UserRole role) => Principal?.IsInRole(role.ToString()) ?? false;
}
