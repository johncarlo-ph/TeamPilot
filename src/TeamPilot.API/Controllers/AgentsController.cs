using Microsoft.AspNetCore.Mvc;
using TeamPilot.Application.Agents;
using TeamPilot.Application.Agents.Dtos;

namespace TeamPilot.API.Controllers;

[ApiController]
public class AgentsController(IAgentService agentService) : ControllerBase
{
    [HttpPost("api/projects/{projectId:guid}/agents")]
    public async Task<ActionResult<AgentDto>> Create(Guid projectId, [FromBody] CreateAgentRequest request, CancellationToken cancellationToken)
    {
        var agent = await agentService.CreateAsync(projectId, request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = agent.Id }, agent);
    }

    [HttpGet("api/projects/{projectId:guid}/agents")]
    public async Task<ActionResult<IReadOnlyList<AgentDto>>> List(Guid projectId, CancellationToken cancellationToken)
    {
        var agents = await agentService.ListAsync(projectId, cancellationToken);
        return Ok(agents);
    }

    [HttpGet("api/agents/{id:guid}")]
    public async Task<ActionResult<AgentDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var agent = await agentService.GetByIdAsync(id, cancellationToken);
        return Ok(agent);
    }

    [HttpPut("api/agents/{id:guid}/configuration")]
    public async Task<ActionResult<AgentDto>> UpdateConfiguration(Guid id, [FromBody] UpdateAgentConfigurationRequest request, CancellationToken cancellationToken)
    {
        var agent = await agentService.UpdateConfigurationAsync(id, request, cancellationToken);
        return Ok(agent);
    }

    [HttpPut("api/agents/{id:guid}/status")]
    public async Task<ActionResult<AgentDto>> UpdateStatus(Guid id, [FromBody] UpdateAgentStatusRequest request, CancellationToken cancellationToken)
    {
        var agent = await agentService.UpdateStatusAsync(id, request, cancellationToken);
        return Ok(agent);
    }
}
