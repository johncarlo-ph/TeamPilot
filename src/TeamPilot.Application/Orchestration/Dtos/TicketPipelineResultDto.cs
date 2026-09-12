using TeamPilot.Application.Tickets.Dtos;

namespace TeamPilot.Application.Orchestration.Dtos;

public sealed record TicketPipelineResultDto(
    TicketDto Ticket,
    IReadOnlyList<AgentWorkResultDto> Steps,
    bool TestingPassed,
    int TestingAttempts);
