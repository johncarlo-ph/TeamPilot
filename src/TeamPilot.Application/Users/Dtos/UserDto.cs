using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Users.Dtos;

public sealed record UserDto(
    Guid Id,
    string Name,
    string Email,
    IReadOnlyCollection<UserRole> Roles,
    UserStatus Status,
    DateTime CreatedAtUtc);
