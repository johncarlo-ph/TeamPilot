using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Agents.Dtos;

public sealed record CreateAgentRequest(string Name, AgentRole Role, string? ConfigurationJson);
