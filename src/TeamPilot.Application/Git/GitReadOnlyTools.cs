using System.Text.Json;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Llm;
using TeamPilot.Domain.Entities;

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
    /// Same two tool definitions as <see cref="Definitions"/>, but when a stage also has
    /// read-only access to other projects (see <see cref="TeamPilot.Application.Orchestration.OrchestrationService"/>'s
    /// cross-project mention grant), both schemas gain an optional <c>"project"</c> input naming
    /// which repository to read from. Used only by callers with more than one accessible
    /// project - a caller with just its own (every other caller, including the Live Agent chat)
    /// keeps using the plain <see cref="Definitions"/> with the original single-repo schema.
    /// </summary>
    public static IReadOnlyList<LlmToolDefinition> BuildDefinitions(IReadOnlyCollection<string> additionalProjectNames)
    {
        if (additionalProjectNames.Count == 0)
        {
            return Definitions;
        }

        var projectNamesList = string.Join(", ", additionalProjectNames);
        var projectPropertyDescription = $"Optional. Which project's repository to read from - one of {projectNamesList}. Omit to read from this ticket's own project.";

        var listFilesSchema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new
            {
                path = new { type = "string", description = "Relative directory path within the repository. Omit or leave empty to list the whole repository." },
                project = new { type = "string", description = projectPropertyDescription },
            },
        });

        var readFileSchema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new
            {
                path = new { type = "string", description = "Relative file path within the repository to read." },
                project = new { type = "string", description = projectPropertyDescription },
            },
            required = new[] { "path" },
        });

        return
        [
            new LlmToolDefinition("list_files", Definitions[0].Description, listFilesSchema),
            new LlmToolDefinition("read_file", Definitions[1].Description, readFileSchema),
        ];
    }

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

    /// <summary>
    /// Multi-repo variant of <see cref="TryExecuteAsync(IGitService, string, string?, LlmToolUseBlock, CancellationToken)"/>
    /// for a stage that also has read-only access to projects referenced by the ticket's
    /// description mentions (see <see cref="TeamPilot.Application.Orchestration.OrchestrationService"/>).
    /// Reads the tool input's optional <c>"project"</c> argument (see <see cref="BuildDefinitions"/>)
    /// and resolves it case-insensitively against <paramref name="allowedProjectsByName"/> (which
    /// includes <paramref name="primaryProject"/> itself, under its own name); an unresolvable
    /// name returns a clear error listing the valid ones instead of silently falling back.
    /// Omitting <c>"project"</c> resolves to <paramref name="primaryProject"/>, exactly like the
    /// single-repo overload. Only the primary project's reads use <paramref name="branchName"/>
    /// (the ticket's own branch) - a referenced project has no "ticket branch" of its own, so its
    /// reads pass <see langword="null"/> (whatever's currently checked out / the repo's default).
    /// </summary>
    public static async Task<(string ResultText, bool IsError)?> TryExecuteAsync(
        IGitService gitService,
        Project primaryProject,
        IReadOnlyDictionary<string, Project> allowedProjectsByName,
        string? branchName,
        LlmToolUseBlock toolUse,
        CancellationToken cancellationToken)
    {
        if (toolUse.Name is not ("list_files" or "read_file"))
        {
            return null;
        }

        var requestedProjectName = TryGetProjectArgument(toolUse);
        if (requestedProjectName is null)
        {
            return await TryExecuteAsync(gitService, primaryProject.RepositoryPath, branchName, toolUse, cancellationToken);
        }

        if (!allowedProjectsByName.TryGetValue(requestedProjectName, out var resolvedProject))
        {
            var validNames = string.Join(", ", allowedProjectsByName.Keys.Select(n => $"\"{n}\""));
            return ($"Unknown project '{requestedProjectName}'. Valid projects: {validNames}.", true);
        }

        var resolvedBranchName = resolvedProject.Id == primaryProject.Id ? branchName : null;
        return await TryExecuteAsync(gitService, resolvedProject.RepositoryPath, resolvedBranchName, toolUse, cancellationToken);
    }

    private static string? TryGetProjectArgument(LlmToolUseBlock toolUse)
    {
        if (string.IsNullOrWhiteSpace(toolUse.InputJson))
        {
            return null;
        }

        try
        {
            using var input = JsonDocument.Parse(toolUse.InputJson);
            return TryGetString(input, "project");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryGetString(JsonDocument input, string propertyName) =>
        input.RootElement.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
