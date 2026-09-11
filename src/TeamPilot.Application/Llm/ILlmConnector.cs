namespace TeamPilot.Application.Llm;

/// <summary>
/// Abstraction over a configurable LLM provider (Claude, OpenAI, Mistral, ...). Concrete
/// providers are implemented in Infrastructure and selected via configuration, so callers
/// never depend on a specific provider.
/// </summary>
public interface ILlmConnector
{
    Task<LlmResponse> SendPromptAsync(LlmRequest request, CancellationToken cancellationToken = default);
}

public sealed record LlmRequest(string Prompt, int MaxTokens = 1024);

public sealed record LlmResponse(string Content, string Model, int InputTokens, int OutputTokens);
