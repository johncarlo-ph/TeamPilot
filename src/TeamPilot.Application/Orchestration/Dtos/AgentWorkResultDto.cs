using TeamPilot.Application.Commits.Dtos;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Orchestration.Dtos;

public sealed record AgentWorkResultDto(
    Guid TicketId,
    Guid AgentId,
    AgentRole AgentRole,
    string LlmOutput,
    CommitDto? Commit);
