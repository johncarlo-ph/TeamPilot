using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Dashboard.Dtos;

/// <summary>SprintId/SprintName are null when the ticket is still in the project's backlog.</summary>
public sealed record AttentionTicketDto(
    Guid Id,
    string Title,
    TicketStatus Status,
    Guid ProjectId,
    string ProjectName,
    Guid? SprintId,
    string? SprintName);
