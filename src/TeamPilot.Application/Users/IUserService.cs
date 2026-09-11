using TeamPilot.Application.Users.Dtos;

namespace TeamPilot.Application.Users;

public interface IUserService
{
    Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<UserDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<UserDto> SetRolesAsync(Guid id, SetUserRolesRequest request, CancellationToken cancellationToken = default);

    Task<UserDto> SetStatusAsync(Guid id, SetUserStatusRequest request, CancellationToken cancellationToken = default);

    Task SetAssignedProjectsAsync(Guid id, SetUserProjectsRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Guid>> GetAssignedProjectIdsAsync(Guid id, CancellationToken cancellationToken = default);
}
