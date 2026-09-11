namespace TeamPilot.Application.Common.Exceptions;

/// <summary>
/// Thrown when external identity validation fails, a refresh token is invalid/expired, or
/// the account is disabled. Maps to HTTP 401.
/// </summary>
public sealed class AuthenticationFailedException : Exception
{
    public AuthenticationFailedException(string message)
        : base(message)
    {
    }
}
