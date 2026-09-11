using TeamPilot.Domain.Entities;

namespace TeamPilot.Application.Auth;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes every currently-active refresh token for a user - used both when reuse of an
    /// already-rotated token is detected (kill the whole session family) and when an admin
    /// disables the account.
    /// </summary>
    Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
