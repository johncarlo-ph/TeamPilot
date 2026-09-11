using TeamPilot.Application.Auth;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Users.Dtos;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Users;

public sealed class UserService(
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IUnitOfWork unitOfWork) : IUserService
{
    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var users = await userRepository.ListAsync(cancellationToken);
        return users.Select(ToDto).ToList();
    }

    public async Task<UserDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(User), id);

        return ToDto(user);
    }

    public async Task<UserDto> SetRolesAsync(Guid id, SetUserRolesRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(User), id);

        user.SetRoles(request.Roles);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(user);
    }

    public async Task<UserDto> SetStatusAsync(Guid id, SetUserStatusRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(User), id);

        if (request.Status == UserStatus.Disabled)
        {
            user.Disable();

            // Disabling an account must kill any sessions it already holds, not just block
            // future logins.
            await refreshTokenRepository.RevokeAllForUserAsync(id, cancellationToken);
        }
        else
        {
            user.Enable();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(user);
    }

    public async Task SetAssignedProjectsAsync(Guid id, SetUserProjectsRequest request, CancellationToken cancellationToken = default)
    {
        _ = await userRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(User), id);

        await userRepository.SetAssignedProjectsAsync(id, request.ProjectIds, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public Task<IReadOnlyCollection<Guid>> GetAssignedProjectIdsAsync(Guid id, CancellationToken cancellationToken = default) =>
        userRepository.GetAssignedProjectIdsAsync(id, cancellationToken);

    private static UserDto ToDto(User user) => new(user.Id, user.Name, user.Email, user.Roles, user.Status, user.CreatedAtUtc);
}
