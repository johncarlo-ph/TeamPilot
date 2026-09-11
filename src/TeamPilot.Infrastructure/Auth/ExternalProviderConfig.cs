namespace TeamPilot.Infrastructure.Auth;

/// <summary>
/// Per-provider OIDC settings, bound from <c>Auth:Providers:{ProviderName}</c> (e.g. "Google",
/// "Microsoft") into a <c>Dictionary&lt;string, ExternalProviderConfig&gt;</c>.
/// </summary>
public class ExternalProviderConfig
{
    /// <summary>
    /// The provider's OpenID Connect discovery document URL, e.g.
    /// "https://accounts.google.com/.well-known/openid-configuration".
    /// </summary>
    public string MetadataAddress { get; set; } = string.Empty;

    /// <summary>
    /// Our OAuth client id registered with that provider - the id_token's `aud` must match.
    /// </summary>
    public string Audience { get; set; } = string.Empty;
}
