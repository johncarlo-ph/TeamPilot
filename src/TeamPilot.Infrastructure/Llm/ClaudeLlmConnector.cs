using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Polly.Timeout;
using TeamPilot.Application.Common.Exceptions;
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

        // The resilience handler registered in AddLlmConnector owns request timing (attempt +
        // total budget, both derived from Llm:TimeoutSeconds) - leaving HttpClient's own Timeout
        // at a finite value here would race it and could cut a retry attempt short.
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
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

        using var httpResponse = await SendAsync(httpRequest, cancellationToken);

        var body = await httpResponse.Content.ReadFromJsonAsync<ClaudeMessageResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Claude API returned an empty response.");

        var content = string.Join(Environment.NewLine, body.Content.Select(c => c.Text));

        return new LlmResponse(content, body.Model, body.Usage.InputTokens, body.Usage.OutputTokens);
    }

    public async Task<LlmConversationResponse> SendConversationAsync(LlmConversationRequest request, CancellationToken cancellationToken = default)
    {
        var messages = new JsonArray();
        foreach (var message in request.Messages)
        {
            var content = new JsonArray();
            foreach (var block in message.Content)
            {
                content.Add(ToAnthropicBlock(block));
            }

            messages.Add(new JsonObject { ["role"] = message.Role, ["content"] = content });
        }

        var body = new JsonObject
        {
            ["model"] = _options.Model,
            ["max_tokens"] = request.MaxTokens,
            ["messages"] = messages,
        };

        if (!string.IsNullOrWhiteSpace(request.System))
        {
            body["system"] = request.System;
        }

        if (request.Tools is { Count: > 0 })
        {
            var tools = new JsonArray();
            foreach (var tool in request.Tools)
            {
                tools.Add(new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["input_schema"] = JsonNode.Parse(tool.InputSchemaJson),
                });
            }

            body["tools"] = tools;
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/messages");
        httpRequest.Headers.Add("x-api-key", _options.ApiKey);
        httpRequest.Headers.Add("anthropic-version", AnthropicVersion);
        httpRequest.Content = JsonContent.Create(body);

        using var httpResponse = await SendAsync(httpRequest, cancellationToken);

        var json = await httpResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken)
            ?? throw new InvalidOperationException("Claude API returned an empty response.");

        var contentBlocks = new List<LlmContentBlock>();
        foreach (var block in json["content"]!.AsArray())
        {
            var type = block!["type"]!.GetValue<string>();
            contentBlocks.Add(type switch
            {
                "text" => new LlmTextBlock(block["text"]!.GetValue<string>()),
                "tool_use" => new LlmToolUseBlock(
                    block["id"]!.GetValue<string>(),
                    block["name"]!.GetValue<string>(),
                    block["input"]!.ToJsonString()),
                _ => new LlmTextBlock(string.Empty),
            });
        }

        var model = json["model"]!.GetValue<string>();
        var stopReason = json["stop_reason"]?.GetValue<string>() ?? "end_turn";
        var usage = json["usage"]!.AsObject();
        var inputTokens = usage["input_tokens"]!.GetValue<int>();
        var outputTokens = usage["output_tokens"]!.GetValue<int>();

        return new LlmConversationResponse(contentBlocks, stopReason, model, inputTokens, outputTokens);
    }

    /// <summary>
    /// Sends the request and ensures a success status, wrapping any failure that survives the
    /// resilience pipeline's retries (see <c>InfrastructureServiceCollectionExtensions.AddLlmConnector</c>)
    /// as <see cref="LlmOperationException"/> - mirrors how <c>LibGit2SharpGitService</c> wraps
    /// <c>LibGit2SharpException</c> as <see cref="GitOperationException"/>, so a client-facing,
    /// actionable error reaches the caller (and, for <c>OrchestrationService</c>, blocks the
    /// ticket) instead of a raw <see cref="HttpRequestException"/>/<see cref="TimeoutRejectedException"/>.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage httpRequest, CancellationToken cancellationToken)
    {
        try
        {
            var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken);
            httpResponse.EnsureSuccessStatusCode();
            return httpResponse;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException)
        {
            throw new LlmOperationException("Claude API call failed.", ex);
        }
    }

    private static JsonObject ToAnthropicBlock(LlmContentBlock block) => block switch
    {
        LlmTextBlock t => new JsonObject { ["type"] = "text", ["text"] = t.Text },
        LlmToolUseBlock u => new JsonObject { ["type"] = "tool_use", ["id"] = u.Id, ["name"] = u.Name, ["input"] = JsonNode.Parse(u.InputJson) },
        LlmToolResultBlock r => new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = r.ToolUseId, ["content"] = r.Content, ["is_error"] = r.IsError },
        _ => throw new NotSupportedException($"Unsupported content block type: {block.GetType().Name}"),
    };

    private sealed record ClaudeMessageResponse(
        [property: JsonPropertyName("content")] IReadOnlyList<ClaudeContentBlock> Content,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("usage")] ClaudeUsage Usage);

    private sealed record ClaudeContentBlock([property: JsonPropertyName("text")] string Text);

    private sealed record ClaudeUsage(
        [property: JsonPropertyName("input_tokens")] int InputTokens,
        [property: JsonPropertyName("output_tokens")] int OutputTokens);
}
