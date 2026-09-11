using TeamPilot.Application.Orchestration.Dtos;
using TeamPilot.Application.Tickets.Dtos;

namespace TeamPilot.Application.Orchestration;

public interface IOrchestrationService
{
    /// <summary>
    /// Assigns one or more sub-agents (Research, Design, Coding, ...) to a ticket, moving it
    /// out of To Do on the first assignment.
    /// </summary>
    Task<TicketDto> AssignSubAgentsAsync(Guid ticketId, AssignSubAgentsRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Has an assigned agent do its work for the ticket via the configured LLM connector.
    /// A Coding agent's output is committed to the ticket's branch and recorded as a
    /// <c>Commit</c>; other roles' output is returned and logged.
    /// </summary>
    Task<AgentWorkResultDto> ExecuteAgentWorkAsync(Guid ticketId, Guid agentId, CancellationToken cancellationToken = default);
}
