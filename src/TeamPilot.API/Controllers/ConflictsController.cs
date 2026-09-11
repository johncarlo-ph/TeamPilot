using Microsoft.AspNetCore.Mvc;
using TeamPilot.Application.Conflicts;
using TeamPilot.Application.Conflicts.Dtos;

namespace TeamPilot.API.Controllers;

[ApiController]
public class ConflictsController(IConflictResolutionService conflictResolutionService) : ControllerBase
{
    [HttpGet("api/tickets/{ticketId:guid}/conflicts")]
    public async Task<ActionResult<IReadOnlyList<ConflictDto>>> ListByTicket(Guid ticketId, CancellationToken cancellationToken)
    {
        var conflicts = await conflictResolutionService.ListByTicketAsync(ticketId, cancellationToken);
        return Ok(conflicts);
    }

    [HttpPost("api/tickets/{ticketId:guid}/conflicts/detect")]
    public async Task<ActionResult<IReadOnlyList<ConflictDto>>> DetectConflicts(Guid ticketId, CancellationToken cancellationToken)
    {
        var conflicts = await conflictResolutionService.DetectConflictsAsync(ticketId, cancellationToken);
        return Ok(conflicts);
    }

    [HttpPost("api/conflicts/{id:guid}/suggest-resolution")]
    public async Task<ActionResult<ConflictDto>> SuggestResolution(Guid id, CancellationToken cancellationToken)
    {
        var conflict = await conflictResolutionService.SuggestResolutionAsync(id, cancellationToken);
        return Ok(conflict);
    }

    [HttpPost("api/conflicts/{id:guid}/resolve-manually")]
    public async Task<ActionResult<ConflictDto>> ResolveManually(Guid id, [FromBody] ResolveConflictManuallyRequest request, CancellationToken cancellationToken)
    {
        var conflict = await conflictResolutionService.ResolveManuallyAsync(id, request, cancellationToken);
        return Ok(conflict);
    }

    [HttpPost("api/conflicts/{id:guid}/accept-ai-suggestion")]
    public async Task<ActionResult<ConflictDto>> AcceptAiSuggestion(Guid id, [FromBody] AcceptAiSuggestionRequest request, CancellationToken cancellationToken)
    {
        var conflict = await conflictResolutionService.AcceptAiSuggestionAsync(id, request, cancellationToken);
        return Ok(conflict);
    }
}
