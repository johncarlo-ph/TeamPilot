using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Agents.Dtos;

public sealed record UpdateAgentStatusRequest(AgentStatus Status);
