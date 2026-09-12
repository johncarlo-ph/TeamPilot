using Microsoft.AspNetCore.Mvc;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Git;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Tickets;
using TeamPilot.Application.Tickets.Dtos;
using TeamPilot.Domain.Entities;

namespace TeamPilot.API.Controllers;

[ApiController]
public class GitController(
    ITicketService ticketService,
    IProjectRepository projectRepository,
    IProjectAccessGuard projectAccessGuard,
    IGitService gitService) : ControllerBase
{
    [HttpPost("api/git/branches")]
    public async Task<ActionResult<TicketDto>> CreateBranch([FromBody] CreateBranchRequest request, CancellationToken cancellationToken)
    {
        var ticket = await ticketService.LinkBranchAsync(request.TicketId, request.BranchName, cancellationToken);
        return Ok(ticket);
    }

    [HttpDelete("api/git/branches/{ticketId:guid}")]
    public async Task<ActionResult<TicketDto>> DeleteBranch(Guid ticketId, CancellationToken cancellationToken)
    {
        var ticket = await ticketService.DeleteBranchAsync(ticketId, cancellationToken);
        return Ok(ticket);
    }

    [HttpGet("api/git/branches/exists")]
    public async Task<ActionResult<bool>> BranchExists([FromQuery] Guid ticketId, [FromQuery] string branchName, CancellationToken cancellationToken)
    {
        var exists = await ticketService.BranchExistsAsync(ticketId, branchName, cancellationToken);
        return Ok(exists);
    }

    [HttpGet("api/projects/{projectId:guid}/git/diff")]
    public async Task<ActionResult<GitDiffResult>> GetDiff(Guid projectId, [FromQuery] string source, [FromQuery] string target, CancellationToken cancellationToken)
    {
        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);

        var project = await projectRepository.GetByIdAsync(projectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), projectId);

        var diff = await gitService.GetDiffAsync(project.RepositoryPath, source, target, cancellationToken);
        return Ok(diff);
    }

    [HttpGet("api/projects/{projectId:guid}/git/conflicts")]
    public async Task<ActionResult<GitMergeConflictResult>> DetectConflicts(Guid projectId, [FromQuery] string source, [FromQuery] string target, CancellationToken cancellationToken)
    {
        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);

        var project = await projectRepository.GetByIdAsync(projectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), projectId);

        var result = await gitService.DetectMergeConflictsAsync(project.RepositoryPath, source, target, cancellationToken);
        return Ok(result);
    }
}
