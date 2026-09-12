using Moq;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Users;
using TeamPilot.Application.Users.Dtos;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using Xunit;

namespace TeamPilot.Application.Tests.Users;

public class UserServiceTests
{
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepository = new();
    private readonly Mock<IAuditLogger> _auditLogger = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly UserService _sut;

    public UserServiceTests()
    {
        _sut = new UserService(_userRepository.Object, _refreshTokenRepository.Object, _auditLogger.Object, _unitOfWork.Object);
    }

    [Fact]
    public async Task SetRolesAsync_WhenUserExists_ReplacesRoles()
    {
        var user = User.Create("Jane Doe", "jane.doe@example.com");
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await _sut.SetRolesAsync(user.Id, new SetUserRolesRequest([UserRole.Admin, UserRole.Developer]));

        Assert.Contains(UserRole.Admin, result.Roles);
        Assert.Contains(UserRole.Developer, result.Roles);
    }

    [Fact]
    public async Task SetRolesAsync_WhenUserDoesNotExist_ThrowsNotFoundException()
    {
        _userRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.SetRolesAsync(Guid.NewGuid(), new SetUserRolesRequest([UserRole.Admin])));
    }

    [Fact]
    public async Task SetStatusAsync_WhenDisabling_DisablesUserAndRevokesAllSessions()
    {
        var user = User.Create("Jane Doe", "jane.doe@example.com");
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await _sut.SetStatusAsync(user.Id, new SetUserStatusRequest(UserStatus.Disabled));

        Assert.Equal(UserStatus.Disabled, result.Status);
        _refreshTokenRepository.Verify(r => r.RevokeAllForUserAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetStatusAsync_WhenEnabling_DoesNotRevokeSessions()
    {
        var user = User.Create("Jane Doe", "jane.doe@example.com");
        user.Disable();
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await _sut.SetStatusAsync(user.Id, new SetUserStatusRequest(UserStatus.Active));

        Assert.Equal(UserStatus.Active, result.Status);
        _refreshTokenRepository.Verify(r => r.RevokeAllForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetAssignedProjectsAsync_WhenUserExists_DelegatesToRepository()
    {
        var user = User.Create("Jane Doe", "jane.doe@example.com");
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var projectIds = new[] { Guid.NewGuid(), Guid.NewGuid() };

        await _sut.SetAssignedProjectsAsync(user.Id, new SetUserProjectsRequest(projectIds));

        _userRepository.Verify(r => r.SetAssignedProjectsAsync(user.Id, projectIds, It.IsAny<CancellationToken>()), Times.Once);
    }
}
