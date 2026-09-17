using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
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
    public void Create_StartsInCloningStatusWithNoFailureReason()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main");

        Assert.Equal(ProjectStatus.Cloning, project.Status);
        Assert.Null(project.CloneFailureReason);
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
    public void Create_WithNoSprintFields_LeavesThemNull()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main");

        Assert.Null(project.SprintStartDate);
        Assert.Null(project.SprintEndDate);
        Assert.Null(project.SprintGoal);
    }

    [Fact]
    public void Create_WithValidSprintFields_SetsThem()
    {
        var start = new DateTime(2026, 1, 1);
        var end = new DateTime(2026, 1, 14);

        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main", start, end, "Ship the sprint fields feature");

        Assert.Equal(start, project.SprintStartDate);
        Assert.Equal(end, project.SprintEndDate);
        Assert.Equal("Ship the sprint fields feature", project.SprintGoal);
    }

    [Fact]
    public void Create_WithSprintEndDateBeforeStartDate_ThrowsArgumentException()
    {
        var start = new DateTime(2026, 1, 14);
        var end = new DateTime(2026, 1, 1);

        Assert.Throws<ArgumentException>(() => Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main", start, end));
    }

    [Fact]
    public void UpdateDetails_WithSprintEndDateBeforeStartDate_ThrowsArgumentException()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main");
        var start = new DateTime(2026, 1, 14);
        var end = new DateTime(2026, 1, 1);

        Assert.Throws<ArgumentException>(() => project.UpdateDetails("TeamPilot", "desc", "main", start, end));
    }

    [Fact]
    public void UpdateDetails_WithSprintFields_UpdatesThem()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main");
        var start = new DateTime(2026, 2, 1);
        var end = new DateTime(2026, 2, 14);

        project.UpdateDetails("TeamPilot", "desc", "main", start, end, "Sprint 2 goal");

        Assert.Equal(start, project.SprintStartDate);
        Assert.Equal(end, project.SprintEndDate);
        Assert.Equal("Sprint 2 goal", project.SprintGoal);
    }

    [Fact]
    public void MarkCloned_WithValidPath_SetsRepositoryPathAndReadyStatus()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main");

        project.MarkCloned("C:/git-sandboxes/" + project.Id);

        Assert.Equal("C:/git-sandboxes/" + project.Id, project.RepositoryPath);
        Assert.Equal(ProjectStatus.Ready, project.Status);
        Assert.Null(project.CloneFailureReason);
    }

    [Fact]
    public void MarkCloned_AfterAPriorFailure_ClearsTheFailureReason()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main");
        project.MarkCloneFailed("Could not clone.");

        project.MarkCloned("C:/git-sandboxes/" + project.Id);

        Assert.Equal(ProjectStatus.Ready, project.Status);
        Assert.Null(project.CloneFailureReason);
    }

    [Fact]
    public void MarkCloneFailed_WithAReason_SetsFailedStatusAndReason()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main");

        project.MarkCloneFailed("Could not clone 'https://github.com/org/teampilot.git'.");

        Assert.Equal(ProjectStatus.Failed, project.Status);
        Assert.Equal("Could not clone 'https://github.com/org/teampilot.git'.", project.CloneFailureReason);
        Assert.Equal(string.Empty, project.RepositoryPath);
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

    [Fact]
    public void Create_StartsNotRemoved()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main");

        Assert.False(project.IsRemoved);
    }

    [Fact]
    public void Remove_SetsIsRemovedAndUpdatedAtUtc()
    {
        var project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main");

        project.Remove();

        Assert.True(project.IsRemoved);
        Assert.NotNull(project.UpdatedAtUtc);
    }
}
