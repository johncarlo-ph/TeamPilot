using Microsoft.AspNetCore.Mvc;
using TeamPilot.Application.Pipelines;
using TeamPilot.Application.Pipelines.Dtos;

namespace TeamPilot.API.Controllers;

[ApiController]
public class PipelineRunsController(IPipelineService pipelineService) : ControllerBase
{
    [HttpPost("api/projects/{projectId:guid}/pipeline-runs")]
    public async Task<ActionResult<PipelineRunDto>> Trigger(Guid projectId, [FromBody] TriggerPipelineRunRequest request, CancellationToken cancellationToken)
    {
        var pipelineRun = await pipelineService.TriggerAsync(projectId, null, request.TriggerReason, cancellationToken);
        return Ok(pipelineRun);
    }

    [HttpGet("api/projects/{projectId:guid}/pipeline-runs")]
    public async Task<ActionResult<IReadOnlyList<PipelineRunDto>>> ListByProject(Guid projectId, CancellationToken cancellationToken)
    {
        var pipelineRuns = await pipelineService.ListByProjectAsync(projectId, cancellationToken);
        return Ok(pipelineRuns);
    }

    [HttpPost("api/pipeline-runs/{id:guid}/start")]
    public async Task<ActionResult<PipelineRunDto>> Start(Guid id, CancellationToken cancellationToken)
    {
        var pipelineRun = await pipelineService.StartAsync(id, cancellationToken);
        return Ok(pipelineRun);
    }

    [HttpPost("api/pipeline-runs/{id:guid}/complete")]
    public async Task<ActionResult<PipelineRunDto>> Complete(Guid id, [FromBody] CompletePipelineRunRequest request, CancellationToken cancellationToken)
    {
        var pipelineRun = await pipelineService.CompleteAsync(id, request, cancellationToken);
        return Ok(pipelineRun);
    }
}
