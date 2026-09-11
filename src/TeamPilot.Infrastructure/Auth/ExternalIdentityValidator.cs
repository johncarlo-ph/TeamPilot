using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common.Exceptions;

namespace TeamPilot.Infrastructure.Auth;

/// <summary>
/// One generic OIDC id_token validator configured per provider (Google, Microsoft, ...) via
/// <c>Auth:Providers</c> - both are pure OpenID Connect providers, so a single implementation
/// driven by each provider's discovery document is all that's needed.
/// </summary>
public class ExternalIdentityValidator : IExternalIdentityValidator
{
    private readonly Dictionary<string, ExternalProviderConfig> _providers;
    private readonly Dictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> _configManagers =
        new(StringComparer.OrdinalIgnoreCase);

    public ExternalIdentityValidator(IOptions<Dictionary<string, ExternalProviderConfig>> providersOptions)
    {
        _providers = new Dictionary<string, ExternalProviderConfig>(providersOptions.Value, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, config) in _providers)
        {
            _configManagers[name] = new ConfigurationManager<OpenIdConnectConfiguration>(
                config.MetadataAddress,
                new OpenIdConnectConfigurationRetriever());
        }
    }

    public async Task<ExternalIdentity> ValidateAsync(string provider, string idToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idToken) || !_providers.TryGetValue(provider, out var providerConfig))
        {
            throw new AuthenticationFailedException($"Unsupported or missing identity provider '{provider}'.");
        }

        var openIdConfig = await _configManagers[provider].GetConfigurationAsync(cancellationToken);

        var validationParameters = new TokenValidationParameters
        {
            ValidIssuer = openIdConfig.Issuer,
            ValidAudience = providerConfig.Audience,
            IssuerSigningKeys = openIdConfig.SigningKeys,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
        };

        // MapInboundClaims defaults to true, which silently renames "sub"/"email"/"name" to the
        // long ClaimTypes.* URIs - disabled so the claim lookups below (by short OIDC name) work,
        // matching the same fix already applied to the JWT bearer options in Program.cs.
        var tokenHandler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        System.Security.Claims.ClaimsPrincipal principal;
        try
        {
            principal = tokenHandler.ValidateToken(idToken, validationParameters, out _);
        }
        catch (Exception ex)
        {
            throw new AuthenticationFailedException($"Invalid {provider} id_token: {ex.Message}");
        }

        var subject = principal.FindFirst("sub")?.Value
            ?? throw new AuthenticationFailedException("id_token is missing a subject claim.");

        var email = principal.FindFirst("email")?.Value
            ?? throw new AuthenticationFailedException("id_token is missing an email claim.");

        var name = principal.FindFirst("name")?.Value;

        return new ExternalIdentity(provider, subject, email, name);
    }
}
