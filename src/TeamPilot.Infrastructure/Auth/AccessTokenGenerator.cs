using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TeamPilot.Application.Auth;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Infrastructure.Auth;

public class AccessTokenGenerator(IOptions<JwtOptions> options) : IAccessTokenGenerator
{
    private static readonly Dictionary<UserRole, string[]> RoleScopes = new()
    {
        [UserRole.Admin] = ["admin", "tickets:read", "tickets:write", "tickets:approve", "users:manage", "projects:manage"],
        [UserRole.Developer] = ["tickets:read", "tickets:write", "tickets:approve"],
        [UserRole.Analyst] = ["tickets:read", "tickets:write"],
    };

    private readonly JwtOptions _options = options.Value;

    public AccessTokenResult Generate(User user)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(_options.AccessTokenLifetimeMinutes);

        var scope = user.Roles
            .SelectMany(role => RoleScopes.TryGetValue(role, out var scopes) ? scopes : [])
            .Distinct()
            .OrderBy(s => s, StringComparer.Ordinal);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Name, user.Name),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("scope", string.Join(' ', scope)),
        };

        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role.ToString())));

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        var tokenValue = new JwtSecurityTokenHandler().WriteToken(token);

        return new AccessTokenResult(tokenValue, expiresAt);
    }
}
