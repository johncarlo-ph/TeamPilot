using System.Text.RegularExpressions;

namespace TeamPilot.Application.Tickets.Mentions;

/// <summary>
/// Extracts <c>@[Label](ticket:&lt;guid&gt;)</c> / <c>@[Label](project:&lt;guid&gt;)</c> /
/// <c>@[Label](file:&lt;projectGuid&gt;:&lt;path&gt;)</c> mention tokens (inserted by the
/// frontend's mention-autocomplete when a user types "@" - a single trigger char, followed by
/// picking a Project/Ticket/File category - while writing a ticket's description) out of a
/// ticket's plain-text <c>Description</c> - no schema change to <c>Description</c> itself, no
/// separate reference entity for the mentions. A file mention carries the id of the project it
/// belongs to (there's no other way to disambiguate a path when a file can be referenced from more
/// than one project - see <c>Orchestration.OrchestrationService</c>), alongside the path itself.
/// Pure text parsing, no I/O - see <c>TicketService.ValidateMentionsAsync</c> (existence/access
/// validation at creation, including that a cross-project file mention's project must itself be
/// directly mentioned) and <c>Orchestration.OrchestrationService</c> (resolving referenced
/// projects for the Research/Design stages). See docs/application.md.
/// </summary>
public static class MentionParser
{
    private static readonly Regex Pattern = new(
        @"@\[(?<label>[^\]\[]+)\]\((?<type>ticket|project|file):(?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})(?::(?<path>[^)]+))?\)",
        RegexOptions.Compiled);

    public static IReadOnlyList<Mention> Parse(string? description)
    {
        if (string.IsNullOrEmpty(description))
        {
            return [];
        }

        var mentions = new List<Mention>();
        foreach (Match match in Pattern.Matches(description))
        {
            var type = match.Groups["type"].Value switch
            {
                "ticket" => MentionType.Ticket,
                "project" => MentionType.Project,
                _ => MentionType.File,
            };

            var pathGroup = match.Groups["path"];
            mentions.Add(new Mention(
                type,
                Guid.Parse(match.Groups["id"].Value),
                match.Groups["label"].Value,
                pathGroup.Success ? pathGroup.Value : null));
        }

        return mentions;
    }
}

public enum MentionType
{
    Ticket,
    Project,
    File,
}

/// <summary>
/// <paramref name="Id"/> is always a ticket or project id - for <see cref="MentionType.File"/>,
/// it's the id of the project the file at <paramref name="FilePath"/> belongs to (never null for
/// that type; always null otherwise).
/// </summary>
public sealed record Mention(MentionType Type, Guid Id, string Label, string? FilePath = null);
