using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using Xunit;

namespace TeamPilot.Domain.Tests;

public class UserTests
{
    [Fact]
    public void Create_WithValidNameAndEmail_StartsActiveWithNoRoles()
    {
        var user = User.Create("Jane Doe", "Jane.Doe@Example.com");

        Assert.Equal("Jane Doe", user.Name);
        Assert.Equal("jane.doe@example.com", user.Email);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Empty(user.Roles);
    }

    [Fact]
    public void Create_WithEmptyName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => User.Create("   ", "jane.doe@example.com"));
    }

    [Fact]
    public void Create_WithEmptyEmail_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => User.Create("Jane Doe", "   "));
    }

    [Fact]
    public void UpdateName_WithNewName_ChangesName()
    {
        var user = User.Create("Jane Doe", "jane.doe@example.com");

        user.UpdateName("Jane Smith");

        Assert.Equal("Jane Smith", user.Name);
    }

    [Fact]
    public void UpdateName_WithSameName_DoesNotMarkUpdated()
    {
        var user = User.Create("Jane Doe", "jane.doe@example.com");

        user.UpdateName("Jane Doe");

        Assert.Null(user.UpdatedAtUtc);
    }

    [Fact]
    public void SetRoles_ReplacesExistingRoles()
    {
        var user = User.Create("Jane Doe", "jane.doe@example.com");
        user.SetRoles([UserRole.Analyst]);

        user.SetRoles([UserRole.Admin, UserRole.Developer]);

        Assert.False(user.HasRole(UserRole.Analyst));
        Assert.True(user.HasRole(UserRole.Admin));
        Assert.True(user.HasRole(UserRole.Developer));
    }

    [Fact]
    public void SetRoles_WithDuplicateRoles_DeduplicatesThem()
    {
        var user = User.Create("Jane Doe", "jane.doe@example.com");

        user.SetRoles([UserRole.Admin, UserRole.Admin]);

        Assert.Single(user.Roles);
    }

    [Fact]
    public void Disable_SetsStatusToDisabled()
    {
        var user = User.Create("Jane Doe", "jane.doe@example.com");

        user.Disable();

        Assert.Equal(UserStatus.Disabled, user.Status);
    }

    [Fact]
    public void Enable_AfterDisable_SetsStatusBackToActive()
    {
        var user = User.Create("Jane Doe", "jane.doe@example.com");
        user.Disable();

        user.Enable();

        Assert.Equal(UserStatus.Active, user.Status);
    }
}
