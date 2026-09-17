using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamPilot.Application.Sprints;
using TeamPilot.Application.Sprints.Dtos;

namespace TeamPilot.API.Controllers;

[ApiController]
public class SprintsController(ISprintService sprintService) : ControllerBase
{
    [HttpPost("api/projects/{projectId:guid}/sprints")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<SprintDto>> Create(Guid projectId, [FromBody] CreateSprintRequest request, CancellationToken cancellationToken)
    {
        var sprint = await sprintService.CreateAsync(projectId, request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = sprint.Id }, sprint);
    }

    [HttpGet("api/projects/{projectId:guid}/sprints")]
    public async Task<ActionResult<IReadOnlyList<SprintDto>>> List(Guid projectId, CancellationToken cancellationToken)
    {
        var sprints = await sprintService.ListAsync(projectId, cancellationToken);
        return Ok(sprints);
    }

    [HttpGet("api/sprints/{id:guid}")]
    public async Task<ActionResult<SprintDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var sprint = await sprintService.GetByIdAsync(id, cancellationToken);
        return Ok(sprint);
    }

    [HttpPut("api/sprints/{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<SprintDto>> Update(Guid id, [FromBody] UpdateSprintRequest request, CancellationToken cancellationToken)
    {
        var sprint = await sprintService.UpdateAsync(id, request, cancellationToken);
        return Ok(sprint);
    }

    [HttpDelete("api/sprints/{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        await sprintService.RemoveAsync(id, cancellationToken);
        return NoContent();
    }
}
