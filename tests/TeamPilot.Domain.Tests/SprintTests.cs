using TeamPilot.Domain.Entities;
using Xunit;

namespace TeamPilot.Domain.Tests;

public class SprintTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();

    [Fact]
    public void Create_WithValidArguments_SetsAllFields()
    {
        var sprint = Sprint.Create(ProjectId, "Sprint 1", "develop");

        Assert.Equal(ProjectId, sprint.ProjectId);
        Assert.Equal("Sprint 1", sprint.Name);
        Assert.Equal("develop", sprint.BaseBranch);
    }

    [Fact]
    public void Create_WithNoBaseBranch_DefaultsToMain()
    {
        var sprint = Sprint.Create(ProjectId, "Sprint 1", null);

        Assert.Equal("main", sprint.BaseBranch);
    }

    [Fact]
    public void Create_WithEmptyProjectId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Sprint.Create(Guid.Empty, "Sprint 1", "main"));
    }

    [Fact]
    public void Create_WithEmptyName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Sprint.Create(ProjectId, "   ", "main"));
    }

    [Fact]
    public void Create_WithNoSprintFields_LeavesThemNull()
    {
        var sprint = Sprint.Create(ProjectId, "Sprint 1", "main");

        Assert.Null(sprint.SprintStartDate);
        Assert.Null(sprint.SprintEndDate);
        Assert.Null(sprint.SprintGoal);
    }

    [Fact]
    public void Create_WithValidSprintFields_SetsThem()
    {
        var start = new DateTime(2026, 1, 1);
        var end = new DateTime(2026, 1, 14);

        var sprint = Sprint.Create(ProjectId, "Sprint 1", "main", start, end, "Ship the sprint aggregate");

        Assert.Equal(start, sprint.SprintStartDate);
        Assert.Equal(end, sprint.SprintEndDate);
        Assert.Equal("Ship the sprint aggregate", sprint.SprintGoal);
    }

    [Fact]
    public void Create_WithSprintEndDateBeforeStartDate_ThrowsArgumentException()
    {
        var start = new DateTime(2026, 1, 14);
        var end = new DateTime(2026, 1, 1);

        Assert.Throws<ArgumentException>(() => Sprint.Create(ProjectId, "Sprint 1", "main", start, end));
    }

    [Fact]
    public void UpdateDetails_WithValidArguments_UpdatesFields()
    {
        var sprint = Sprint.Create(ProjectId, "Sprint 1", "main");

        sprint.UpdateDetails("Sprint 1 Renamed", "develop");

        Assert.Equal("Sprint 1 Renamed", sprint.Name);
        Assert.Equal("develop", sprint.BaseBranch);
        Assert.NotNull(sprint.UpdatedAtUtc);
    }

    [Fact]
    public void UpdateDetails_WithEmptyName_ThrowsArgumentException()
    {
        var sprint = Sprint.Create(ProjectId, "Sprint 1", "main");

        Assert.Throws<ArgumentException>(() => sprint.UpdateDetails("   ", "main"));
    }

    [Fact]
    public void UpdateDetails_WithEmptyBaseBranch_ThrowsArgumentException()
    {
        var sprint = Sprint.Create(ProjectId, "Sprint 1", "main");

        Assert.Throws<ArgumentException>(() => sprint.UpdateDetails("Sprint 1", "   "));
    }

    [Fact]
    public void UpdateDetails_WithSprintEndDateBeforeStartDate_ThrowsArgumentException()
    {
        var sprint = Sprint.Create(ProjectId, "Sprint 1", "main");
        var start = new DateTime(2026, 1, 14);
        var end = new DateTime(2026, 1, 1);

        Assert.Throws<ArgumentException>(() => sprint.UpdateDetails("Sprint 1", "main", start, end));
    }

    [Fact]
    public void UpdateDetails_WithSprintFields_UpdatesThem()
    {
        var sprint = Sprint.Create(ProjectId, "Sprint 1", "main");
        var start = new DateTime(2026, 2, 1);
        var end = new DateTime(2026, 2, 14);

        sprint.UpdateDetails("Sprint 1", "main", start, end, "Sprint 2 goal");

        Assert.Equal(start, sprint.SprintStartDate);
        Assert.Equal(end, sprint.SprintEndDate);
        Assert.Equal("Sprint 2 goal", sprint.SprintGoal);
    }

    [Fact]
    public void Create_StartsNotRemoved()
    {
        var sprint = Sprint.Create(ProjectId, "Sprint 1", "main");

        Assert.False(sprint.IsRemoved);
    }

    [Fact]
    public void Remove_SetsIsRemovedAndUpdatedAtUtc()
    {
        var sprint = Sprint.Create(ProjectId, "Sprint 1", "main");

        sprint.Remove();

        Assert.True(sprint.IsRemoved);
        Assert.NotNull(sprint.UpdatedAtUtc);
    }
}
