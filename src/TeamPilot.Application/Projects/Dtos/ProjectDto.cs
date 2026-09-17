using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Projects.Dtos;

public sealed record ProjectDto(
    Guid Id,
    string Name,
    string Description,
    string RemoteUrl,
    ProjectStatus Status,
    string? CloneFailureReason,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);
