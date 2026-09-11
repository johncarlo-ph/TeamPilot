namespace TeamPilot.Application.Common.Interfaces;

/// <summary>
/// Encrypts/decrypts a project's remote Git access token for storage at rest. The one concrete
/// implementation (Infrastructure) uses ASP.NET Core Data Protection.
/// </summary>
public interface IGitCredentialProtector
{
    string Protect(string plaintext);

    string Unprotect(string protectedValue);
}
