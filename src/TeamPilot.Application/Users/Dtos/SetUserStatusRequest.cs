using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Users.Dtos;

public sealed record SetUserStatusRequest(UserStatus Status);
