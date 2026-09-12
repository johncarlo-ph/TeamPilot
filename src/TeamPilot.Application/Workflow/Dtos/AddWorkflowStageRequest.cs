namespace TeamPilot.Application.Workflow.Dtos;

/// <summary>Places an already-provisioned-but-unscheduled agent into the project's workflow, at
/// the end of the sequence.</summary>
public sealed record AddWorkflowStageRequest(Guid AgentId);
