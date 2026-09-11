using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Users.Dtos;

public sealed record SetUserRolesRequest(IReadOnlyCollection<UserRole> Roles);
