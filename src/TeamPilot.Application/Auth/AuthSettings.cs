namespace TeamPilot.Application.Auth;

public class AuthSettings
{
    public const string SectionName = "Auth";

    public int RefreshTokenLifetimeDays { get; set; } = 14;

    /// <summary>
    /// Email addresses auto-granted Admin on their first login - the only way to bootstrap
    /// the first Admin, since every other new user starts with zero roles.
    /// </summary>
    public IReadOnlyCollection<string> SeedAdminEmails { get; set; } = [];
}
