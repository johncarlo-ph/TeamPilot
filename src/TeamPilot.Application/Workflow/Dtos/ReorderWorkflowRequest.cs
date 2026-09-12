namespace TeamPilot.Application.Workflow.Dtos;

/// <summary>Every one of the project's stage ids, in the new desired execution order.</summary>
public sealed record ReorderWorkflowRequest(IReadOnlyList<Guid> StageIds);
