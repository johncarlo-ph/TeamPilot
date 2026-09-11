using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Tickets.Dtos;

public sealed record TicketAgentAssignmentDto(Guid AgentId, AgentRole RoleAtAssignment, DateTime AssignedAtUtc);
