using Moq;
using TeamPilot.Application.Common;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Users;
using TeamPilot.Domain.Enums;
using Xunit;

namespace TeamPilot.Application.Tests.Common;

public class ProjectAccessGuardTests
{
    private readonly Mock<ICurrentUserContext> _currentUser = new();
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly ProjectAccessGuard _sut;

    public ProjectAccessGuardTests()
    {
        _sut = new ProjectAccessGuard(_currentUser.Object, _userRepository.Object);
    }

    [Fact]
    public async Task EnsureAccessAsync_WhenCallerIsAdmin_NeverChecksAssignments()
    {
        _currentUser.Setup(c => c.IsInRole(UserRole.Admin)).Returns(true);

        await _sut.EnsureAccessAsync(Guid.NewGuid());

        _userRepository.Verify(
            r => r.GetAssignedProjectIdsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EnsureAccessAsync_WhenProjectIsAssigned_DoesNotThrow()
    {
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        _currentUser.Setup(c => c.IsInRole(UserRole.Admin)).Returns(false);
        _currentUser.Setup(c => c.UserId).Returns(userId);
        _userRepository
            .Setup(r => r.GetAssignedProjectIdsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([projectId]);

        await _sut.EnsureAccessAsync(projectId);
    }

    [Fact]
    public async Task EnsureAccessAsync_WhenProjectIsNotAssigned_ThrowsForbiddenException()
    {
        var userId = Guid.NewGuid();

        _currentUser.Setup(c => c.IsInRole(UserRole.Admin)).Returns(false);
        _currentUser.Setup(c => c.UserId).Returns(userId);
        _userRepository
            .Setup(r => r.GetAssignedProjectIdsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.EnsureAccessAsync(Guid.NewGuid()));
    }
}
