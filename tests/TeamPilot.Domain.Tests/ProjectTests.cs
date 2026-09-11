using TeamPilot.Domain.Entities;
using Xunit;

namespace TeamPilot.Domain.Tests;

public class ProjectTests
{
    [Fact]
    public void Create_WithValidArguments_SetsAllFields()
    {
        var project = Project.Create("TeamPilot", "AI ticketing system", "https://github.com/org/teampilot.git", "encrypted-token", "develop");

        Assert.Equal("TeamPilot", project.Name);
        Assert.Equal("AI ticketing system", project.Description);
        Assert.Equal("https://github.com/org/teampilot.git", project.RemoteUrl);
        Assert.Equal("encrypted-token", project.EncryptedAccessToken);
        Assert.Equal("develop", project.BaseBranch);
        Assert.Equal(string.Empty, project.RepositoryPath);
    }

    [Fact]
    public void Create_WithNoBaseBranch_DefaultsToMain()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", null);

        Assert.Equal("main", project.BaseBranch);
    }

    [Fact]
    public void Create_WithEmptyName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Project.Create("   ", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main"));
    }

    [Fact]
    public void Create_WithEmptyRemoteUrl_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Project.Create("TeamPilot", "desc", "   ", "encrypted-token", "main"));
    }

    [Fact]
    public void Create_WithEmptyAccessToken_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "   ", "main"));
    }

    [Fact]
    public void AssignSandboxPath_WithValidPath_SetsRepositoryPath()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main");

        project.AssignSandboxPath("C:/git-sandboxes/" + project.Id);

        Assert.Equal("C:/git-sandboxes/" + project.Id, project.RepositoryPath);
    }

    [Fact]
    public void UpdateDetails_WithValidArguments_UpdatesFields()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main");

        project.UpdateDetails("TeamPilot Renamed", "New description", "develop");

        Assert.Equal("TeamPilot Renamed", project.Name);
        Assert.Equal("New description", project.Description);
        Assert.Equal("develop", project.BaseBranch);
        Assert.NotNull(project.UpdatedAtUtc);
    }

    [Fact]
    public void UpdateDetails_DoesNotChangeRemoteUrl()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main");

        project.UpdateDetails("TeamPilot Renamed", "New description", "develop");

        Assert.Equal("https://github.com/org/teampilot.git", project.RemoteUrl);
    }

    [Fact]
    public void RotateAccessToken_WithValidToken_ReplacesEncryptedToken()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main");

        project.RotateAccessToken("new-encrypted-token");

        Assert.Equal("new-encrypted-token", project.EncryptedAccessToken);
        Assert.NotNull(project.UpdatedAtUtc);
    }
}
