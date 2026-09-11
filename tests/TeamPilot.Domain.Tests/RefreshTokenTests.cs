using TeamPilot.Domain.Entities;
using Xunit;

namespace TeamPilot.Domain.Tests;

public class RefreshTokenTests
{
    [Fact]
    public void Create_WithFutureExpiry_IsActive()
    {
        var token = RefreshToken.Create(Guid.NewGuid(), "hash-value", DateTime.UtcNow.AddDays(1));

        Assert.True(token.IsActive);
        Assert.False(token.IsRevoked);
        Assert.False(token.IsExpired);
    }

    [Fact]
    public void Create_WithPastExpiry_IsExpired()
    {
        var token = RefreshToken.Create(Guid.NewGuid(), "hash-value", DateTime.UtcNow.AddDays(-1));

        Assert.True(token.IsExpired);
        Assert.False(token.IsActive);
    }

    [Fact]
    public void Revoke_SetsIsRevokedAndClearsIsActive()
    {
        var token = RefreshToken.Create(Guid.NewGuid(), "hash-value", DateTime.UtcNow.AddDays(1));

        token.Revoke();

        Assert.True(token.IsRevoked);
        Assert.False(token.IsActive);
        Assert.NotNull(token.RevokedAtUtc);
    }

    [Fact]
    public void Revoke_WithReplacementTokenId_RecordsIt()
    {
        var token = RefreshToken.Create(Guid.NewGuid(), "hash-value", DateTime.UtcNow.AddDays(1));
        var replacementId = Guid.NewGuid();

        token.Revoke(replacementId);

        Assert.Equal(replacementId, token.ReplacedByTokenId);
    }

    [Fact]
    public void Revoke_WhenAlreadyRevoked_IsIdempotent()
    {
        var token = RefreshToken.Create(Guid.NewGuid(), "hash-value", DateTime.UtcNow.AddDays(1));
        token.Revoke();
        var firstRevokedAt = token.RevokedAtUtc;

        token.Revoke();

        Assert.Equal(firstRevokedAt, token.RevokedAtUtc);
    }
}
