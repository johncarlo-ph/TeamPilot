using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamPilot.Application.Agents.Dtos;
using TeamPilot.Application.Workflow;
using TeamPilot.Application.Workflow.Dtos;

namespace TeamPilot.API.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}/workflow")]
public class WorkflowController(IWorkflowService workflowService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WorkflowStageDto>>> List(Guid projectId, CancellationToken cancellationToken)
    {
        var stages = await workflowService.ListAsync(projectId, cancellationToken);
        return Ok(stages);
    }

    [HttpGet("unscheduled-agents")]
    public async Task<ActionResult<IReadOnlyList<AgentDto>>> ListUnscheduledAgents(Guid projectId, CancellationToken cancellationToken)
    {
        var agents = await workflowService.ListUnscheduledAgentsAsync(projectId, cancellationToken);
        return Ok(agents);
    }

    [HttpPost("agents")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<AgentDto>> CreateCustomAgent(Guid projectId, [FromBody] CreateCustomAgentRequest request, CancellationToken cancellationToken)
    {
        var agent = await workflowService.CreateCustomAgentAsync(projectId, request, cancellationToken);
        return Ok(agent);
    }

    [HttpDelete("agents/{agentId:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteCustomAgent(Guid projectId, Guid agentId, CancellationToken cancellationToken)
    {
        await workflowService.DeleteCustomAgentAsync(projectId, agentId, cancellationToken);
        return NoContent();
    }

    [HttpPost("stages")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<WorkflowStageDto>> AddStage(Guid projectId, [FromBody] AddWorkflowStageRequest request, CancellationToken cancellationToken)
    {
        var stage = await workflowService.AddExistingAgentAsync(projectId, request, cancellationToken);
        return Ok(stage);
    }

    [HttpDelete("stages/{stageId:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RemoveStage(Guid projectId, Guid stageId, CancellationToken cancellationToken)
    {
        await workflowService.RemoveStageAsync(projectId, stageId, cancellationToken);
        return NoContent();
    }

    [HttpPut("order")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IReadOnlyList<WorkflowStageDto>>> Reorder(Guid projectId, [FromBody] ReorderWorkflowRequest request, CancellationToken cancellationToken)
    {
        var stages = await workflowService.ReorderAsync(projectId, request, cancellationToken);
        return Ok(stages);
    }

    [HttpPut("stages/{stageId:guid}/loop-back")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<WorkflowStageDto>> SetLoopBack(Guid projectId, Guid stageId, [FromBody] SetWorkflowLoopBackRequest request, CancellationToken cancellationToken)
    {
        var stage = await workflowService.SetLoopBackAsync(projectId, stageId, request, cancellationToken);
        return Ok(stage);
    }

    [HttpDelete("stages/{stageId:guid}/loop-back")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<WorkflowStageDto>> ClearLoopBack(Guid projectId, Guid stageId, CancellationToken cancellationToken)
    {
        var stage = await workflowService.ClearLoopBackAsync(projectId, stageId, cancellationToken);
        return Ok(stage);
    }
}
