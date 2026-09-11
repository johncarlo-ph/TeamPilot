using Microsoft.AspNetCore.DataProtection;
using TeamPilot.Application.Common.Interfaces;

namespace TeamPilot.Infrastructure.Git;

/// <summary>
/// Encrypts project access tokens at rest using ASP.NET Core Data Protection. Note: Data
/// Protection's default key ring is persisted per-machine - if the API is ever scaled out to
/// multiple instances/containers, the key ring must be persisted somewhere shared (e.g. a file
/// share or blob store) or a token encrypted on one instance won't decrypt on another.
/// </summary>
public class DataProtectionGitCredentialProtector : IGitCredentialProtector
{
    private const string Purpose = "TeamPilot.Git.AccessToken.v1";

    private readonly IDataProtector _protector;

    public DataProtectionGitCredentialProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}
