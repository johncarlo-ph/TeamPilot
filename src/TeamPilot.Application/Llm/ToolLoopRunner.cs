namespace TeamPilot.Application.Llm;

/// <summary>
/// Runs a bounded Claude tool-use loop against <see cref="ILlmConnector.SendConversationAsync"/>:
/// sends <paramref name="messages"/>, and for as long as the model keeps requesting tools,
/// dispatches each one via <paramref name="executeToolAsync"/> and feeds the (truncated) results
/// back as a new turn, until the model stops asking for tools or <paramref name="maxRoundtrips"/>
/// is exhausted. Shared by the Live Agent chat and the Research/Design/Coding pipeline stages so
/// both get the same bounded, truncated tool-use mechanics instead of duplicating them.
/// </summary>
public static class ToolLoopRunner
{
    public const int DefaultMaxRoundtrips = 6;
    public const int DefaultMaxToolResultChars = 8000;
    public const string DefaultFallbackText = "I couldn't finish that within my available steps.";

    public static async Task<string> RunAsync(
        ILlmConnector llmConnector,
        List<LlmMessage> messages,
        string? system,
        IReadOnlyList<LlmToolDefinition> tools,
        int maxTokens,
        Func<LlmToolUseBlock, CancellationToken, Task<(string ResultText, bool IsError)>> executeToolAsync,
        CancellationToken cancellationToken,
        int maxRoundtrips = DefaultMaxRoundtrips,
        int maxToolResultChars = DefaultMaxToolResultChars,
        string fallbackText = DefaultFallbackText,
        Action<int, LlmConversationResponse>? onRound = null)
    {
        for (var round = 0; round < maxRoundtrips; round++)
        {
            var response = await llmConnector.SendConversationAsync(
                new LlmConversationRequest(messages, maxTokens, system, tools),
                cancellationToken);

            onRound?.Invoke(round, response);

            if (response.StopReason != "tool_use")
            {
                return response.TextContent;
            }

            messages.Add(new LlmMessage("assistant", response.Content));

            var toolResults = new List<LlmContentBlock>();
            foreach (var toolUse in response.Content.OfType<LlmToolUseBlock>())
            {
                var (resultText, isError) = await executeToolAsync(toolUse, cancellationToken);
                toolResults.Add(new LlmToolResultBlock(toolUse.Id, Truncate(resultText, maxToolResultChars), isError));
            }

            messages.Add(new LlmMessage("user", toolResults));
        }

        return fallbackText;
    }

    private static string Truncate(string text, int maxChars) =>
        text.Length > maxChars ? text[..maxChars] + "\n\n[truncated]" : text;
}
