using System.Security.Cryptography;
using System.Text;
using TeamPilot.Application.Auth;

namespace TeamPilot.Infrastructure.Auth;

public class RefreshTokenGenerator : IRefreshTokenGenerator
{
    public string GenerateValue() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    public string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
