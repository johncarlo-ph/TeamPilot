namespace TeamPilot.Application.Common.Exceptions;

/// <summary>
/// Thrown when a ticket description "@"-mentions a file in a project other than the ticket's own,
/// without that project also being directly "@project"-mentioned somewhere in the same
/// description. A cross-project file reference is only meaningful once the project itself is
/// referenced - both because that's what actually grants Research/Design read access to it (see
/// <c>Orchestration.OrchestrationService.ResolveReferencedProjectsAsync</c>), and so a file chip
/// alone can't silently smuggle in an unreviewed cross-project pointer.
/// </summary>
public sealed class FileMentionProjectNotReferencedException(string projectName, string filePath)
    : Exception($"The file '{filePath}' is in project '{projectName}' - that project must also be @-mentioned (as a Project reference) before a file in it can be @-mentioned.")
{
}
