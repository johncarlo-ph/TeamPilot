using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using Xunit;

namespace TeamPilot.Domain.Tests;

public class AgentTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();

    [Fact]
    public void Create_WithEmptyProjectId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Agent.Create(Guid.Empty, "Coder", AgentRole.Coding));
    }

    [Theory]
    [InlineData(AgentRole.Research)]
    [InlineData(AgentRole.Design)]
    [InlineData(AgentRole.Coding)]
    [InlineData(AgentRole.Testing)]
    [InlineData(AgentRole.LiveAgent)]
    public void Create_SeedsCurrentDefaultInstructionForEveryType(AgentRole role)
    {
        var agent = Agent.Create(ProjectId, "Agent", role);

        Assert.Equal(3, agent.Instructions.Count);
        foreach (var type in Enum.GetValues<InstructionType>())
        {
            var current = Assert.Single(agent.Instructions, i => i.Type == type);
            Assert.Equal(1, current.Version);
            Assert.True(current.IsCurrent);
            Assert.False(string.IsNullOrWhiteSpace(current.Content));
        }
    }

    [Fact]
    public void AddInstructionVersion_AfterDefaultSeed_SupersedesDefaultAndBecomesVersionTwo()
    {
        var agent = Agent.Create(ProjectId, "Coder", AgentRole.Coding);
        var seeded = agent.Instructions.Single(i => i.Type == InstructionType.Guideline);

        var updated = agent.AddInstructionVersion(InstructionType.Guideline, "Follow SOLID", "admin");

        Assert.False(seeded.IsCurrent);
        Assert.True(updated.IsCurrent);
        Assert.Equal(2, updated.Version);
    }

    [Fact]
    public void AddInstructionVersion_SecondVersionOfSameType_SupersedesPreviousVersion()
    {
        var agent = Agent.Create(ProjectId, "Coder", AgentRole.Coding);
        var first = agent.AddInstructionVersion(InstructionType.Guideline, "v1", "admin");

        var second = agent.AddInstructionVersion(InstructionType.Guideline, "v2", "admin");

        Assert.False(first.IsCurrent);
        Assert.True(second.IsCurrent);
        Assert.Equal(3, second.Version);
    }

    [Fact]
    public void AddInstructionVersion_DifferentTypes_VersionsEachTypeIndependently()
    {
        var agent = Agent.Create(ProjectId, "Coder", AgentRole.Coding);

        var guideline = agent.AddInstructionVersion(InstructionType.Guideline, "g1", "admin");
        var requirement = agent.AddInstructionVersion(InstructionType.Requirement, "r1", "admin");

        Assert.Equal(2, guideline.Version);
        Assert.Equal(2, requirement.Version);
        Assert.True(guideline.IsCurrent);
        Assert.True(requirement.IsCurrent);
    }
}
