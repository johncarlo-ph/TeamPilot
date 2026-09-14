using System.Text.Json;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Llm;

namespace TeamPilot.Application.Git;

/// <summary>
/// The read-only, sandboxed <c>list_files</c>/<c>read_file</c> tool definitions and dispatch
/// shared by every <see cref="ToolLoopRunner"/> user that needs to inspect a project's repository
/// sandbox - the Live Agent chat, and the Research/Design/Coding pipeline stages. Never writes;
/// each call is independently wrapped in its own brief <see cref="GitRepositoryLock"/> scope
/// rather than one held for an entire tool-use loop, so passing a specific
/// <paramref name="branchName"/> (see <see cref="TryExecuteAsync"/>) on every call is what lets a
/// ticket-branch-specific caller safely share one project's sandbox clone with everything else
/// touching it, without ever holding the lock across LLM latency.
/// </summary>
public static class GitReadOnlyTools
{
    public static readonly IReadOnlyList<LlmToolDefinition> Definitions =
    [
        new LlmToolDefinition(
            "list_files",
            "List every file recursively under a path within this project's repository (read-only, sandboxed), " +
            "as paths relative to that path - so one call shows a whole subtree instead of just one directory " +
            "level. Prefer a specific, narrow path over the repository root so you see relevant files in fewer " +
            "calls; the result says if it was truncated and needs a narrower path to see the rest.",
            """{"type":"object","properties":{"path":{"type":"string","description":"Relative directory path within the repository. Omit or leave empty to list the whole repository."}}}"""),
        new LlmToolDefinition(
            "read_file",
            "Read the contents of one file within this project's repository (read-only, sandboxed). " +
            "Only read files that are directly relevant to your current task.",
            """{"type":"object","properties":{"path":{"type":"string","description":"Relative file path within the repository to read."}},"required":["path"]}"""),
    ];

    /// <summary>
    /// Dispatches <paramref name="toolUse"/> if it names <c>list_files</c> or <c>read_file</c>,
    /// returning <see langword="null"/> for any other tool name so a caller with tools of its own
    /// can fall through to its own dispatch. When <paramref name="branchName"/> is given, that
    /// branch is checked out before the read (see <see cref="IGitService.ListFilesAsync"/>/
    /// <see cref="IGitService.ReadFileAsync"/>); pass <see langword="null"/> to read whatever is
    /// currently checked out instead (the Live Agent chat's usage).
    /// </summary>
    public static async Task<(string ResultText, bool IsError)?> TryExecuteAsync(
        IGitService gitService,
        string repositoryPath,
        string? branchName,
        LlmToolUseBlock toolUse,
        CancellationToken cancellationToken)
    {
        if (toolUse.Name is not ("list_files" or "read_file"))
        {
            return null;
        }

        JsonDocument input;
        try
        {
            input = JsonDocument.Parse(string.IsNullOrWhiteSpace(toolUse.InputJson) ? "{}" : toolUse.InputJson);
        }
        catch (JsonException)
        {
            return ("Invalid tool input.", true);
        }

        using (input)
        {
            try
            {
                if (toolUse.Name == "list_files")
                {
                    var path = TryGetString(input, "path");
                    IReadOnlyList<string> entries;
                    await using (await GitRepositoryLock.AcquireAsync(repositoryPath, cancellationToken))
                    {
                        entries = await gitService.ListFilesAsync(repositoryPath, path, branchName, cancellationToken);
                    }

                    return (entries.Count == 0 ? "(empty)" : string.Join("\n", entries), false);
                }

                var relativeFilePath = TryGetString(input, "path") ?? string.Empty;
                GitFileReadResult result;
                await using (await GitRepositoryLock.AcquireAsync(repositoryPath, cancellationToken))
                {
                    result = await gitService.ReadFileAsync(repositoryPath, relativeFilePath, branchName, cancellationToken);
                }

                if (!result.Found)
                {
                    return ("File not found or not accessible.", true);
                }

                var text = result.Truncated ? $"{result.Content}\n\n[truncated]" : result.Content!;
                return (text, false);
            }
            catch (GitOperationException ex)
            {
                return (ex.Message, true);
            }
        }
    }

    private static string? TryGetString(JsonDocument input, string propertyName) =>
        input.RootElement.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
