namespace TeamPilot.Application.Auth;

/// <summary>
/// Generates the opaque, high-entropy refresh token value handed to the client, and hashes
/// it for storage (the raw value is never persisted).
/// </summary>
public interface IRefreshTokenGenerator
{
    string GenerateValue();

    string Hash(string value);
}
