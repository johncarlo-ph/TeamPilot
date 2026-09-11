using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TeamPilot.Application.Llm;

namespace TeamPilot.Infrastructure.Llm;

/// <summary>
/// Calls Anthropic's Messages API. Registered behind <see cref="ILlmConnector"/> so
/// additional providers (OpenAI, Mistral, ...) can be added later without touching callers.
/// </summary>
public class ClaudeLlmConnector : ILlmConnector
{
    private const string AnthropicVersion = "2023-06-01";

    private readonly HttpClient _httpClient;
    private readonly LlmOptions _options;

    public ClaudeLlmConnector(HttpClient httpClient, IOptions<LlmOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.BaseAddress ??= new Uri(_options.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public async Task<LlmResponse> SendPromptAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/messages");
        httpRequest.Headers.Add("x-api-key", _options.ApiKey);
        httpRequest.Headers.Add("anthropic-version", AnthropicVersion);
        httpRequest.Content = JsonContent.Create(new
        {
            model = _options.Model,
            max_tokens = request.MaxTokens,
            messages = new[] { new { role = "user", content = request.Prompt } },
        });

        using var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken);
        httpResponse.EnsureSuccessStatusCode();

        var body = await httpResponse.Content.ReadFromJsonAsync<ClaudeMessageResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Claude API returned an empty response.");

        var content = string.Join(Environment.NewLine, body.Content.Select(c => c.Text));

        return new LlmResponse(content, body.Model, body.Usage.InputTokens, body.Usage.OutputTokens);
    }

    private sealed record ClaudeMessageResponse(
        [property: JsonPropertyName("content")] IReadOnlyList<ClaudeContentBlock> Content,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("usage")] ClaudeUsage Usage);

    private sealed record ClaudeContentBlock([property: JsonPropertyName("text")] string Text);

    private sealed record ClaudeUsage(
        [property: JsonPropertyName("input_tokens")] int InputTokens,
        [property: JsonPropertyName("output_tokens")] int OutputTokens);
}
