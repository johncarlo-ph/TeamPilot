using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamPilot.Application.InstructionTemplates;
using TeamPilot.Application.InstructionTemplates.Dtos;
using TeamPilot.Domain.Enums;

namespace TeamPilot.API.Controllers;

[ApiController]
[Route("api/instruction-templates")]
public class InstructionTemplatesController(IInstructionTemplateService instructionTemplateService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InstructionTemplateDto>>> List([FromQuery] AgentRole? role, [FromQuery] InstructionType? type, CancellationToken cancellationToken)
    {
        var templates = await instructionTemplateService.ListAsync(role, type, cancellationToken);
        return Ok(templates);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InstructionTemplateDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var template = await instructionTemplateService.GetByIdAsync(id, cancellationToken);
        return Ok(template);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<InstructionTemplateDto>> Create([FromBody] CreateInstructionTemplateRequest request, CancellationToken cancellationToken)
    {
        var template = await instructionTemplateService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = template.Id }, template);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<InstructionTemplateDto>> Update(Guid id, [FromBody] UpdateInstructionTemplateRequest request, CancellationToken cancellationToken)
    {
        var template = await instructionTemplateService.UpdateAsync(id, request, cancellationToken);
        return Ok(template);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await instructionTemplateService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
