using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Pipelines.Dtos;

public sealed record PipelineRunDto(
    Guid Id,
    Guid ProjectId,
    Guid? TicketId,
    PipelineRunStatus Status,
    string TriggerReason,
    string? LogOutput,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime CreatedAtUtc);
