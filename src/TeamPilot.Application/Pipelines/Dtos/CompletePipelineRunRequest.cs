namespace TeamPilot.Application.Pipelines.Dtos;

public sealed record CompletePipelineRunRequest(bool Succeeded, string? LogOutput);
