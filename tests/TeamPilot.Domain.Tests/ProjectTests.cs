using TeamPilot.Domain.Entities;
using Xunit;

namespace TeamPilot.Domain.Tests;

public class ProjectTests
{
    [Fact]
    public void Create_WithValidArguments_SetsAllFields()
    {
        var project = Project.Create("TeamPilot", "AI ticketing system", "C:/repos/teampilot");

        Assert.Equal("TeamPilot", project.Name);
        Assert.Equal("AI ticketing system", project.Description);
        Assert.Equal("C:/repos/teampilot", project.RepositoryPath);
    }

    [Fact]
    public void Create_WithEmptyName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Project.Create("   ", "desc", "C:/repos/teampilot"));
    }

    [Fact]
    public void Create_WithEmptyRepositoryPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Project.Create("TeamPilot", "desc", "   "));
    }

    [Fact]
    public void UpdateDetails_WithValidArguments_UpdatesFields()
    {
        var project = Project.Create("TeamPilot", "desc", "C:/repos/teampilot");

        project.UpdateDetails("TeamPilot Renamed", "New description", "C:/repos/teampilot-v2");

        Assert.Equal("TeamPilot Renamed", project.Name);
        Assert.Equal("New description", project.Description);
        Assert.Equal("C:/repos/teampilot-v2", project.RepositoryPath);
        Assert.NotNull(project.UpdatedAtUtc);
    }
}
