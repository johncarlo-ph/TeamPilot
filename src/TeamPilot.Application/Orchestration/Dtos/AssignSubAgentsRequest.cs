namespace TeamPilot.Application.Orchestration.Dtos;

public sealed record AssignSubAgentsRequest(IReadOnlyCollection<Guid> AgentIds);
