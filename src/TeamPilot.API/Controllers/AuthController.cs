using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Auth.Dtos;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Users.Dtos;
using TeamPilot.Domain.Enums;

namespace TeamPilot.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService, ICurrentUserContext currentUser) : ControllerBase
{
    private const string RefreshTokenCookieName = "teampilot_rt";
    private const string RefreshTokenCookiePath = "/api/auth";

    [HttpPost("login/{provider}")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(string provider, [FromBody] ExternalLoginRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(provider, request.IdToken, GetClientIpAddress(), cancellationToken);
        SetRefreshTokenCookie(result.RefreshToken, result.RefreshTokenExpiresAtUtc);

        return Ok(new LoginResponse(result.AccessToken, result.AccessTokenExpiresAtUtc, result.User));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Refresh(CancellationToken cancellationToken)
    {
        var refreshToken = Request.Cookies[RefreshTokenCookieName];
        if (string.IsNullOrEmpty(refreshToken))
        {
            return Unauthorized();
        }

        var result = await authService.RefreshAsync(refreshToken, GetClientIpAddress(), cancellationToken);
        SetRefreshTokenCookie(result.RefreshToken, result.RefreshTokenExpiresAtUtc);

        return Ok(new LoginResponse(result.AccessToken, result.AccessTokenExpiresAtUtc, result.User));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var refreshToken = Request.Cookies[RefreshTokenCookieName];
        if (!string.IsNullOrEmpty(refreshToken))
        {
            await authService.LogoutAsync(refreshToken, GetClientIpAddress(), cancellationToken);
        }

        Response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions { Path = RefreshTokenCookiePath });
        return NoContent();
    }

    [HttpGet("me")]
    public ActionResult<CurrentUserResponse> Me()
    {
        if (!currentUser.IsAuthenticated)
        {
            return Unauthorized();
        }

        return Ok(new CurrentUserResponse(currentUser.UserId, currentUser.Name, currentUser.Email, currentUser.Roles));
    }

    private string? GetClientIpAddress() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private void SetRefreshTokenCookie(string refreshToken, DateTime expiresAtUtc)
    {
        Response.Cookies.Append(RefreshTokenCookieName, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = RefreshTokenCookiePath,
            Expires = new DateTimeOffset(expiresAtUtc, TimeSpan.Zero),
        });
    }
}

public sealed record LoginResponse(string AccessToken, DateTime AccessTokenExpiresAtUtc, UserDto User);

public sealed record CurrentUserResponse(Guid UserId, string? Name, string? Email, IReadOnlyCollection<UserRole> Roles);
