namespace TeamPilot.Infrastructure.Git;

public class GitOptions
{
    public const string SectionName = "Git";

    public string DefaultAuthorEmail { get; set; } = "teampilot@local";

    /// <summary>Root folder under which each project's server-managed sandbox clone lives, one
    /// subfolder per project id. Resolved against the host's content root when relative.</summary>
    public string SandboxRoot { get; set; } = "git-sandboxes";
}
