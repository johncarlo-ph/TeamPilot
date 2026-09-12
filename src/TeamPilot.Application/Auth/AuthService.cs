using Microsoft.Extensions.Options;
using TeamPilot.Application.Auth.Dtos;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Users;
using TeamPilot.Application.Users.Dtos;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Auth;

public sealed class AuthService(
    IExternalIdentityValidator externalIdentityValidator,
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IAccessTokenGenerator accessTokenGenerator,
    IRefreshTokenGenerator refreshTokenGenerator,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork,
    IOptions<AuthSettings> authSettings) : IAuthService
{
    private readonly AuthSettings _authSettings = authSettings.Value;

    public async Task<LoginResultDto> LoginAsync(string provider, string idToken, string? ipAddress, CancellationToken cancellationToken = default)
    {
        ExternalIdentity identity;
        try
        {
            identity = await externalIdentityValidator.ValidateAsync(provider, idToken, cancellationToken);
        }
        catch (AuthenticationFailedException)
        {
            await auditLogger.LogAsync(AuditEventType.LoginFailed, null, $"Invalid {provider} id_token.", ipAddress, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw;
        }

        var normalizedEmail = identity.Email.Trim().ToLowerInvariant();
        var displayName = string.IsNullOrWhiteSpace(identity.Name) ? identity.Email : identity.Name;
        var user = await userRepository.GetByEmailAsync(normalizedEmail, cancellationToken);

        if (user is null)
        {
            user = User.Create(displayName, normalizedEmail);

            if (_authSettings.SeedAdminEmails.Any(seedEmail => string.Equals(seedEmail.Trim(), normalizedEmail, StringComparison.OrdinalIgnoreCase)))
            {
                user.SetRoles([UserRole.Admin]);
            }

            await userRepository.AddAsync(user, cancellationToken);
        }
        else
        {
            user.UpdateName(displayName);
        }

        if (user.Status == UserStatus.Disabled)
        {
            await auditLogger.LogAsync(AuditEventType.LoginFailed, user.Id, "Account is disabled.", ipAddress, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new AuthenticationFailedException("This account has been disabled.");
        }

        var result = await IssueTokensAsync(user, cancellationToken);
        await auditLogger.LogAsync(AuditEventType.LoginSucceeded, user.Id, $"Provider: {provider}.", ipAddress, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return result;
    }

    public async Task<LoginResultDto> RefreshAsync(string refreshTokenValue, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var tokenHash = refreshTokenGenerator.Hash(refreshTokenValue);
        var existingToken = await refreshTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken)
            ?? throw new AuthenticationFailedException("Invalid refresh token.");

        if (existingToken.IsRevoked)
        {
            // Someone presented a token that was already rotated away - treat as a
            // compromised session and kill every active token for this user.
            await refreshTokenRepository.RevokeAllForUserAsync(existingToken.UserId, cancellationToken);
            await auditLogger.LogAsync(AuditEventType.TokenReuseDetected, existingToken.UserId, null, ipAddress, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new AuthenticationFailedException("This refresh token has already been used. All sessions have been revoked.");
        }

        if (existingToken.IsExpired)
        {
            throw new AuthenticationFailedException("Refresh token has expired.");
        }

        var user = await userRepository.GetByIdAsync(existingToken.UserId, cancellationToken)
            ?? throw new AuthenticationFailedException("User no longer exists.");

        if (user.Status == UserStatus.Disabled)
        {
            throw new AuthenticationFailedException("This account has been disabled.");
        }

        var result = await IssueTokensAsync(user, cancellationToken, existingToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return result;
    }

    public async Task LogoutAsync(string refreshTokenValue, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var tokenHash = refreshTokenGenerator.Hash(refreshTokenValue);
        var existingToken = await refreshTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (existingToken is null || existingToken.IsRevoked)
        {
            return;
        }

        existingToken.Revoke();
        await auditLogger.LogAsync(AuditEventType.Logout, existingToken.UserId, null, ipAddress, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<LoginResultDto> IssueTokensAsync(User user, CancellationToken cancellationToken, RefreshToken? tokenToRotate = null)
    {
        var accessToken = accessTokenGenerator.Generate(user);

        var refreshTokenValue = refreshTokenGenerator.GenerateValue();
        var refreshTokenHash = refreshTokenGenerator.Hash(refreshTokenValue);
        var refreshTokenExpiresAt = DateTime.UtcNow.AddDays(_authSettings.RefreshTokenLifetimeDays);
        var newRefreshToken = RefreshToken.Create(user.Id, refreshTokenHash, refreshTokenExpiresAt);

        await refreshTokenRepository.AddAsync(newRefreshToken, cancellationToken);
        tokenToRotate?.Revoke(newRefreshToken.Id);

        return new LoginResultDto(
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            refreshTokenValue,
            refreshTokenExpiresAt,
            new UserDto(user.Id, user.Name, user.Email, user.Roles, user.Status, user.CreatedAtUtc));
    }
}
