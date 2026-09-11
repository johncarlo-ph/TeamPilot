using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamPilot.Application.Users;
using TeamPilot.Application.Users.Dtos;

namespace TeamPilot.API.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = "Admin")]
public class UsersController(IUserService userService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> List(CancellationToken cancellationToken)
    {
        var users = await userService.ListAsync(cancellationToken);
        return Ok(users);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var user = await userService.GetByIdAsync(id, cancellationToken);
        return Ok(user);
    }

    [HttpPut("{id:guid}/roles")]
    public async Task<ActionResult<UserDto>> SetRoles(Guid id, [FromBody] SetUserRolesRequest request, CancellationToken cancellationToken)
    {
        var user = await userService.SetRolesAsync(id, request, cancellationToken);
        return Ok(user);
    }

    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<UserDto>> SetStatus(Guid id, [FromBody] SetUserStatusRequest request, CancellationToken cancellationToken)
    {
        var user = await userService.SetStatusAsync(id, request, cancellationToken);
        return Ok(user);
    }

    [HttpPut("{id:guid}/projects")]
    public async Task<IActionResult> SetAssignedProjects(Guid id, [FromBody] SetUserProjectsRequest request, CancellationToken cancellationToken)
    {
        await userService.SetAssignedProjectsAsync(id, request, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/projects")]
    public async Task<ActionResult<IReadOnlyCollection<Guid>>> GetAssignedProjects(Guid id, CancellationToken cancellationToken)
    {
        var projectIds = await userService.GetAssignedProjectIdsAsync(id, cancellationToken);
        return Ok(projectIds);
    }
}
