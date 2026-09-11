namespace TeamPilot.Infrastructure.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "TeamPilot";

    public string Audience { get; set; } = "TeamPilot.Client";

    public int AccessTokenLifetimeMinutes { get; set; } = 15;
}
