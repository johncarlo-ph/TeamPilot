using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Projects.Dtos;

namespace TeamPilot.API.Controllers;

[ApiController]
[Route("api/projects")]
public class ProjectsController(IProjectService projectService) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ProjectDto>> Create([FromBody] CreateProjectRequest request, CancellationToken cancellationToken)
    {
        var project = await projectService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = project.Id }, project);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProjectDto>>> List(CancellationToken cancellationToken)
    {
        var projects = await projectService.ListAsync(cancellationToken);
        return Ok(projects);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProjectDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var project = await projectService.GetByIdAsync(id, cancellationToken);
        return Ok(project);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ProjectDto>> Update(Guid id, [FromBody] UpdateProjectRequest request, CancellationToken cancellationToken)
    {
        var project = await projectService.UpdateAsync(id, request, cancellationToken);
        return Ok(project);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        await projectService.RemoveAsync(id, cancellationToken);
        return NoContent();
    }
}
