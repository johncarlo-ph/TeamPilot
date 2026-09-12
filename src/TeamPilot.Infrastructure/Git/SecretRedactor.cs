using System.Text.RegularExpressions;

namespace TeamPilot.Infrastructure.Git;

/// <summary>
/// Best-effort regex redaction of common secret shapes from file content before it's returned
/// to the Live Agent's <c>read_file</c> tool. Not a guarantee - a determined secret won't always
/// match one of these patterns - so it's paired with an outright block on well-known
/// secret-bearing paths/extensions in <see cref="LibGit2SharpGitService"/>.
/// </summary>
internal static partial class SecretRedactor
{
    private const string Placeholder = "[REDACTED]";

    private static readonly Regex[] Patterns =
    [
        AwsAccessKeyPattern(),
        PrivateKeyBlockPattern(),
        JwtPattern(),
        ConnectionStringSecretPattern(),
        GenericApiKeyAssignmentPattern(),
    ];

    internal static string Redact(string content)
    {
        foreach (var pattern in Patterns)
        {
            content = pattern.Replace(content, Placeholder);
        }

        return content;
    }

    [GeneratedRegex("AKIA[0-9A-Z]{16}")]
    private static partial Regex AwsAccessKeyPattern();

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----")]
    private static partial Regex PrivateKeyBlockPattern();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}")]
    private static partial Regex JwtPattern();

    [GeneratedRegex(@"(?i)(password|pwd)\s*=\s*[^;""'\r\n]+")]
    private static partial Regex ConnectionStringSecretPattern();

    [GeneratedRegex(@"(?i)(api[_-]?key|secret|token|access[_-]?key)\s*[:=]\s*[""']?[A-Za-z0-9_\-\.]{12,}[""']?")]
    private static partial Regex GenericApiKeyAssignmentPattern();
}
