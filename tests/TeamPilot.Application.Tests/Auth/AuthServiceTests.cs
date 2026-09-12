using Microsoft.Extensions.Options;
using Moq;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Users;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using Xunit;

namespace TeamPilot.Application.Tests.Auth;

public class AuthServiceTests
{
    private readonly Mock<IExternalIdentityValidator> _externalIdentityValidator = new();
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepository = new();
    private readonly Mock<IAccessTokenGenerator> _accessTokenGenerator = new();
    private readonly Mock<IRefreshTokenGenerator> _refreshTokenGenerator = new();
    private readonly Mock<IAuditLogger> _auditLogger = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _accessTokenGenerator
            .Setup(g => g.Generate(It.IsAny<User>()))
            .Returns(new AccessTokenResult("access-token-value", DateTime.UtcNow.AddMinutes(15)));
        _refreshTokenGenerator.Setup(g => g.GenerateValue()).Returns("raw-refresh-token-value");
        _refreshTokenGenerator.Setup(g => g.Hash(It.IsAny<string>())).Returns((string value) => $"hash-of:{value}");

        _sut = new AuthService(
            _externalIdentityValidator.Object,
            _userRepository.Object,
            _refreshTokenRepository.Object,
            _accessTokenGenerator.Object,
            _refreshTokenGenerator.Object,
            _auditLogger.Object,
            _unitOfWork.Object,
            Options.Create(new AuthSettings { RefreshTokenLifetimeDays = 14 }));
    }

    [Fact]
    public async Task LoginAsync_WhenUserDoesNotExist_CreatesNewUserWithNoRoles()
    {
        _externalIdentityValidator
            .Setup(v => v.ValidateAsync("google", "id-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalIdentity("google", "subject-1", "new-user@example.com", "New User"));
        _userRepository.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await _sut.LoginAsync("google", "id-token", "127.0.0.1");

        Assert.Equal("New User", result.User.Name);
        Assert.Equal("new-user@example.com", result.User.Email);
        Assert.Empty(result.User.Roles);
        _userRepository.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Once);
        _auditLogger.Verify(
            a => a.LogAsync(AuditEventType.LoginSucceeded, It.IsAny<Guid?>(), It.IsAny<string>(), "127.0.0.1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WhenUserAlreadyExists_ReusesExistingUserAndSyncsName()
    {
        var existingUser = User.Create("Old Name", "existing@example.com");
        existingUser.SetRoles([UserRole.Analyst]);

        _externalIdentityValidator
            .Setup(v => v.ValidateAsync("google", "id-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalIdentity("google", "subject-1", "existing@example.com", "New Name"));
        _userRepository
            .Setup(r => r.GetByEmailAsync("existing@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingUser);

        var result = await _sut.LoginAsync("google", "id-token", null);

        Assert.Equal(existingUser.Id, result.User.Id);
        Assert.Equal("New Name", result.User.Name);
        Assert.Contains(UserRole.Analyst, result.User.Roles);
        _userRepository.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WhenAccountIsDisabled_ThrowsAuthenticationFailedException()
    {
        var disabledUser = User.Create("Disabled User", "disabled@example.com");
        disabledUser.Disable();

        _externalIdentityValidator
            .Setup(v => v.ValidateAsync("google", "id-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalIdentity("google", "subject-1", "disabled@example.com", "Disabled User"));
        _userRepository
            .Setup(r => r.GetByEmailAsync("disabled@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(disabledUser);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => _sut.LoginAsync("google", "id-token", null));
        _auditLogger.Verify(
            a => a.LogAsync(AuditEventType.LoginFailed, disabledUser.Id, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_WithValidToken_RevokesOldTokenAndIssuesNewOneWithoutAuditLogging()
    {
        var user = User.Create("Jane Doe", "user@example.com");
        var existingToken = RefreshToken.Create(user.Id, "hash-of:old-token-value", DateTime.UtcNow.AddDays(1));

        _refreshTokenGenerator.Setup(g => g.Hash("old-token-value")).Returns("hash-of:old-token-value");
        _refreshTokenRepository
            .Setup(r => r.GetByTokenHashAsync("hash-of:old-token-value", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingToken);
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await _sut.RefreshAsync("old-token-value", null);

        Assert.True(existingToken.IsRevoked);
        Assert.Equal("raw-refresh-token-value", result.RefreshToken);

        // A silent background refresh is not a user-initiated action, so it must not clutter
        // the audit log the way login/logout/token-reuse events do.
        _auditLogger.Verify(
            a => a.LogAsync(It.IsAny<AuditEventType>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RefreshAsync_WhenTokenWasAlreadyRotated_RevokesAllSessionsForThatUser()
    {
        var userId = Guid.NewGuid();
        var alreadyRotatedToken = RefreshToken.Create(userId, "hash-of:reused-token-value", DateTime.UtcNow.AddDays(1));
        alreadyRotatedToken.Revoke(Guid.NewGuid());

        _refreshTokenGenerator.Setup(g => g.Hash("reused-token-value")).Returns("hash-of:reused-token-value");
        _refreshTokenRepository
            .Setup(r => r.GetByTokenHashAsync("hash-of:reused-token-value", It.IsAny<CancellationToken>()))
            .ReturnsAsync(alreadyRotatedToken);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => _sut.RefreshAsync("reused-token-value", null));

        _refreshTokenRepository.Verify(r => r.RevokeAllForUserAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
        _auditLogger.Verify(
            a => a.LogAsync(AuditEventType.TokenReuseDetected, userId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LogoutAsync_WithActiveToken_RevokesIt()
    {
        var user = User.Create("Jane Doe", "user@example.com");
        var existingToken = RefreshToken.Create(user.Id, "hash-of:token-value", DateTime.UtcNow.AddDays(1));

        _refreshTokenGenerator.Setup(g => g.Hash("token-value")).Returns("hash-of:token-value");
        _refreshTokenRepository
            .Setup(r => r.GetByTokenHashAsync("hash-of:token-value", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingToken);

        await _sut.LogoutAsync("token-value", null);

        Assert.True(existingToken.IsRevoked);
        _auditLogger.Verify(
            a => a.LogAsync(AuditEventType.Logout, user.Id, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
