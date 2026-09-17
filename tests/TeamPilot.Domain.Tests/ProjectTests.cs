using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using Xunit;

namespace TeamPilot.Domain.Tests;

public class ProjectTests
{
    [Fact]
    public void Create_WithValidArguments_SetsAllFields()
    {
        var project = Project.Create("TeamPilot", "AI ticketing system", "https://github.com/org/teampilot.git", "encrypted-token");

        Assert.Equal("TeamPilot", project.Name);
        Assert.Equal("AI ticketing system", project.Description);
        Assert.Equal("https://github.com/org/teampilot.git", project.RemoteUrl);
        Assert.Equal("encrypted-token", project.EncryptedAccessToken);
        Assert.Equal(string.Empty, project.RepositoryPath);
    }

    [Fact]
    public void Create_StartsInCloningStatusWithNoFailureReason()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token");

        Assert.Equal(ProjectStatus.Cloning, project.Status);
        Assert.Null(project.CloneFailureReason);
    }

    [Fact]
    public void Create_WithEmptyName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Project.Create("   ", "desc", "https://github.com/org/teampilot.git", "encrypted-token"));
    }

    [Fact]
    public void Create_WithEmptyRemoteUrl_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Project.Create("TeamPilot", "desc", "   ", "encrypted-token"));
    }

    [Fact]
    public void Create_WithEmptyAccessToken_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "   "));
    }

    [Fact]
    public void MarkCloned_WithValidPath_SetsRepositoryPathAndReadyStatus()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token");

        project.MarkCloned("C:/git-sandboxes/" + project.Id);

        Assert.Equal("C:/git-sandboxes/" + project.Id, project.RepositoryPath);
        Assert.Equal(ProjectStatus.Ready, project.Status);
        Assert.Null(project.CloneFailureReason);
    }

    [Fact]
    public void MarkCloned_AfterAPriorFailure_ClearsTheFailureReason()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token");
        project.MarkCloneFailed("Could not clone.");

        project.MarkCloned("C:/git-sandboxes/" + project.Id);

        Assert.Equal(ProjectStatus.Ready, project.Status);
        Assert.Null(project.CloneFailureReason);
    }

    [Fact]
    public void MarkCloneFailed_WithAReason_SetsFailedStatusAndReason()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token");

        project.MarkCloneFailed("Could not clone 'https://github.com/org/teampilot.git'.");

        Assert.Equal(ProjectStatus.Failed, project.Status);
        Assert.Equal("Could not clone 'https://github.com/org/teampilot.git'.", project.CloneFailureReason);
        Assert.Equal(string.Empty, project.RepositoryPath);
    }

    [Fact]
    public void UpdateDetails_WithValidArguments_UpdatesFields()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token");

        project.UpdateDetails("TeamPilot Renamed", "New description");

        Assert.Equal("TeamPilot Renamed", project.Name);
        Assert.Equal("New description", project.Description);
        Assert.NotNull(project.UpdatedAtUtc);
    }

    [Fact]
    public void UpdateDetails_WithEmptyName_ThrowsArgumentException()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token");

        Assert.Throws<ArgumentException>(() => project.UpdateDetails("   ", "New description"));
    }

    [Fact]
    public void UpdateDetails_DoesNotChangeRemoteUrl()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token");

        project.UpdateDetails("TeamPilot Renamed", "New description");

        Assert.Equal("https://github.com/org/teampilot.git", project.RemoteUrl);
    }

    [Fact]
    public void RotateAccessToken_WithValidToken_ReplacesEncryptedToken()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token");

        project.RotateAccessToken("new-encrypted-token");

        Assert.Equal("new-encrypted-token", project.EncryptedAccessToken);
        Assert.NotNull(project.UpdatedAtUtc);
    }

    [Fact]
    public void Create_StartsNotRemoved()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token");

        Assert.False(project.IsRemoved);
    }

    [Fact]
    public void Remove_SetsIsRemovedAndUpdatedAtUtc()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token");

        project.Remove();

        Assert.True(project.IsRemoved);
        Assert.NotNull(project.UpdatedAtUtc);
    }
}
