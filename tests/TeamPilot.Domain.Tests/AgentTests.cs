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

    [Fact]
    public void AddInstructionVersion_FirstVersion_SetsVersionToOneAndIsCurrent()
    {
        var agent = Agent.Create(ProjectId, "Coder", AgentRole.Coding);

        var instruction = agent.AddInstructionVersion(InstructionType.Guideline, "Follow SOLID", "admin");

        Assert.Equal(1, instruction.Version);
        Assert.True(instruction.IsCurrent);
    }

    [Fact]
    public void AddInstructionVersion_SecondVersionOfSameType_SupersedesPreviousVersion()
    {
        var agent = Agent.Create(ProjectId, "Coder", AgentRole.Coding);
        var first = agent.AddInstructionVersion(InstructionType.Guideline, "v1", "admin");

        var second = agent.AddInstructionVersion(InstructionType.Guideline, "v2", "admin");

        Assert.False(first.IsCurrent);
        Assert.True(second.IsCurrent);
        Assert.Equal(2, second.Version);
        Assert.Equal(2, agent.Instructions.Count);
    }

    [Fact]
    public void AddInstructionVersion_DifferentTypes_VersionsEachTypeIndependently()
    {
        var agent = Agent.Create(ProjectId, "Coder", AgentRole.Coding);

        var guideline = agent.AddInstructionVersion(InstructionType.Guideline, "g1", "admin");
        var requirement = agent.AddInstructionVersion(InstructionType.Requirement, "r1", "admin");

        Assert.Equal(1, guideline.Version);
        Assert.Equal(1, requirement.Version);
        Assert.True(guideline.IsCurrent);
        Assert.True(requirement.IsCurrent);
    }
}
