namespace TeamPilot.Application.Workflow.Dtos;

public sealed record SetWorkflowLoopBackRequest(Guid TargetStageId, int MaxLoopIterations);
