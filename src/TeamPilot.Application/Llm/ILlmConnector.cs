namespace TeamPilot.Application.Llm;

/// <summary>
/// Abstraction over a configurable LLM provider (Claude, OpenAI, Mistral, ...). Concrete
/// providers are implemented in Infrastructure and selected via configuration, so callers
/// never depend on a specific provider.
/// </summary>
public interface ILlmConnector
{
    /// <summary>
    /// Single flat prompt, single response - used by the fixed pipeline stages
    /// (Research/Design/Coding/Testing), which need no conversation history or tools.
    /// </summary>
    Task<LlmResponse> SendPromptAsync(LlmRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Multi-turn conversation with optional tool use - used by the Live Agent chat, which needs
    /// message history and the ability to call tools (e.g. reading a repo file) mid-turn. Kept
    /// as a separate method from <see cref="SendPromptAsync"/> rather than folding tools/history
    /// into <see cref="LlmRequest"/>, so the pipeline's simple single-shot call sites are
    /// untouched.
    /// </summary>
    Task<LlmConversationResponse> SendConversationAsync(LlmConversationRequest request, CancellationToken cancellationToken = default);
}

public sealed record LlmRequest(string Prompt, int MaxTokens = 1024);

public sealed record LlmResponse(string Content, string Model, int InputTokens, int OutputTokens);

public sealed record LlmConversationRequest(
    IReadOnlyList<LlmMessage> Messages,
    int MaxTokens = 1024,
    string? System = null,
    IReadOnlyList<LlmToolDefinition>? Tools = null);

public sealed record LlmConversationResponse(
    IReadOnlyList<LlmContentBlock> Content,
    string StopReason,
    string Model,
    int InputTokens,
    int OutputTokens)
{
    public string TextContent => string.Join(Environment.NewLine, Content.OfType<LlmTextBlock>().Select(b => b.Text));
}

public sealed record LlmMessage(string Role, IReadOnlyList<LlmContentBlock> Content)
{
    public static LlmMessage User(string text) => new("user", [new LlmTextBlock(text)]);

    public static LlmMessage Assistant(IReadOnlyList<LlmContentBlock> content) => new("assistant", content);
}

/// <summary>One block of a message's content. Anthropic's Messages API represents a message as
/// an array of typed blocks rather than a single string once tools are involved.</summary>
public abstract record LlmContentBlock;

public sealed record LlmTextBlock(string Text) : LlmContentBlock;

public sealed record LlmToolUseBlock(string Id, string Name, string InputJson) : LlmContentBlock;

public sealed record LlmToolResultBlock(string ToolUseId, string Content, bool IsError = false) : LlmContentBlock;

/// <summary>A tool the model may call. <c>InputSchemaJson</c> is a JSON Schema object (as a
/// string) describing the tool's expected input.</summary>
public sealed record LlmToolDefinition(string Name, string Description, string InputSchemaJson);
