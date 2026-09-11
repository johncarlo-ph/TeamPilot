using TeamPilot.Domain.Entities;

namespace TeamPilot.Application.Auth;

/// <summary>
/// Issues our own signed JWT access tokens (never the external providers' tokens) carrying
/// <c>sub</c>, one <c>role</c> claim per role, and a space-separated <c>scope</c> claim.
/// </summary>
public interface IAccessTokenGenerator
{
    AccessTokenResult Generate(User user);
}

public sealed record AccessTokenResult(string Token, DateTime ExpiresAtUtc);
