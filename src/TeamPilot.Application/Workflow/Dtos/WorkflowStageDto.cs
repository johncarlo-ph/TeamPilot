using TeamPilot.Application.Agents.Dtos;

namespace TeamPilot.Application.Workflow.Dtos;

public sealed record WorkflowStageDto(
    Guid Id,
    Guid ProjectId,
    int Order,
    AgentDto Agent,
    Guid? LoopBackToStageId,
    int? MaxLoopIterations);
