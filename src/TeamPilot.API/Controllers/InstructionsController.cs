using Microsoft.AspNetCore.Mvc;
using TeamPilot.Application.Instructions;
using TeamPilot.Application.Instructions.Dtos;
using TeamPilot.Domain.Enums;

namespace TeamPilot.API.Controllers;

[ApiController]
[Route("api/agents/{agentId:guid}/instructions")]
public class InstructionsController(IInstructionService instructionService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InstructionDto>>> ListByAgent(Guid agentId, CancellationToken cancellationToken)
    {
        var instructions = await instructionService.ListByAgentAsync(agentId, cancellationToken);
        return Ok(instructions);
    }

    [HttpPost]
    public async Task<ActionResult<InstructionDto>> AddVersion(Guid agentId, [FromBody] AddInstructionVersionRequest request, CancellationToken cancellationToken)
    {
        var instruction = await instructionService.AddVersionAsync(agentId, request, cancellationToken);
        return Ok(instruction);
    }

    [HttpGet("current")]
    public async Task<ActionResult<InstructionDto>> GetCurrent(Guid agentId, [FromQuery] InstructionType type, CancellationToken cancellationToken)
    {
        var instruction = await instructionService.GetCurrentAsync(agentId, type, cancellationToken);
        return instruction is null ? NotFound() : Ok(instruction);
    }
}
