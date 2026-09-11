namespace TeamPilot.Application.Auth;

/// <summary>
/// Validates an OpenID Connect id_token issued by an external provider (Google, Microsoft)
/// against that provider's published JWKS - signature, issuer, audience, and expiry.
/// </summary>
public interface IExternalIdentityValidator
{
    /// <summary>
    /// Throws <see cref="TeamPilot.Application.Common.Exceptions.AuthenticationFailedException"/>
    /// if the token is invalid, expired, or from an unsupported provider.
    /// </summary>
    Task<ExternalIdentity> ValidateAsync(string provider, string idToken, CancellationToken cancellationToken = default);
}

public sealed record ExternalIdentity(string Provider, string Subject, string Email, string? Name);
