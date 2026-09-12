namespace TeamPilot.Application.Conflicts.Dtos;

public sealed record ResolveConflictManuallyRequest(string ResolvedContent, string? Note, string ResolvedBy);
