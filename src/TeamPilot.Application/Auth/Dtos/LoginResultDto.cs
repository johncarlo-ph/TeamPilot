using TeamPilot.Application.Users.Dtos;

namespace TeamPilot.Application.Auth.Dtos;

/// <summary>
/// Carries both tokens back to the controller. The controller splits this: the access token
/// goes in the JSON body, the refresh token is set as an httpOnly cookie and never appears
/// in a response body.
/// </summary>
public sealed record LoginResultDto(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    UserDto User);
