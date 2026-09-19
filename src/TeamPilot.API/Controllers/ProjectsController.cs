using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamPilot.Application.Mentions;
using TeamPilot.Application.Mentions.Dtos;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Projects.Dtos;

namespace TeamPilot.API.Controllers;

[ApiController]
[Route("api/projects")]
public class ProjectsController(IProjectService projectService, IMentionSearchService mentionSearchService) : ControllerBase
{
    /// <summary>
    /// Backs the "@" mention-autocomplete dropdown a user gets while writing a ticket's
    /// description - searches by name across every project the caller has access to, capped and
    /// short-query-filtered by <see cref="IMentionSearchService"/> itself.
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<ProjectMentionResultDto>>> Search([FromQuery] string query, CancellationToken cancellationToken)
    {
        var results = await mentionSearchService.SearchProjectsAsync(query, cancellationToken);
        return Ok(results);
    }

    /// <summary>
    /// Backs the "@" mention-autocomplete's File-category search once a project is known (the
    /// ticket's own, or one already directly "@project"-mentioned - see docs/frontend.md) - the
    /// frontend calls this once per eligible project and merges results itself.
    /// </summary>
    [HttpGet("{id:guid}/files/search")]
    public async Task<ActionResult<IReadOnlyList<FileMentionResultDto>>> SearchFiles(Guid id, [FromQuery] string query, CancellationToken cancellationToken)
    {
        var results = await mentionSearchService.SearchFilesAsync(id, query, cancellationToken);
        return Ok(results);
    }

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
