namespace TeamPilot.Infrastructure.Llm;

public class LlmOptions
{
    public const string SectionName = "Llm";

    public string Provider { get; set; } = "Claude";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "claude-sonnet-4-5";

    public string BaseUrl { get; set; } = "https://api.anthropic.com";

    public int TimeoutSeconds { get; set; } = 60;
}
