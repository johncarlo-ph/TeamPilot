using TeamPilot.Application.Tickets.Dtos;

namespace TeamPilot.Application.Orchestration.Dtos;

/// <summary>
/// <paramref name="TestingPassed"/> and <paramref name="TestingAttempts"/> report the outcome of
/// whichever workflow stage has a loop-back configured and ran last (Testing, for the default
/// workflow) - a workflow with no loop-back stage at all reports the "nothing to retry" default
/// of <see langword="true"/>/<c>0</c>.
/// </summary>
public sealed record TicketPipelineResultDto(
    TicketDto Ticket,
    IReadOnlyList<AgentWorkResultDto> Steps,
    bool TestingPassed,
    int TestingAttempts);
