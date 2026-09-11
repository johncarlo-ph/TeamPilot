using TeamPilot.Application.Auth.Dtos;

namespace TeamPilot.Application.Auth;

public interface IAuthService
{
    Task<LoginResultDto> LoginAsync(string provider, string idToken, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotates a refresh token. Reuse of an already-rotated token revokes every active
    /// session for that user (compromise signal).
    /// </summary>
    Task<LoginResultDto> RefreshAsync(string refreshTokenValue, string? ipAddress, CancellationToken cancellationToken = default);

    Task LogoutAsync(string refreshTokenValue, string? ipAddress, CancellationToken cancellationToken = default);
}
