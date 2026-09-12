using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using Xunit;

namespace TeamPilot.Domain.Tests;

public class InstructionTemplateTests
{
    [Fact]
    public void Create_WithValidInput_SetsAllFields()
    {
        var template = InstructionTemplate.Create("Strict TDD", AgentRole.Coding, InstructionType.Guideline, "Write the test first.");

        Assert.Equal("Strict TDD", template.Name);
        Assert.Equal(AgentRole.Coding, template.Role);
        Assert.Equal(InstructionType.Guideline, template.Type);
        Assert.Equal("Write the test first.", template.Content);
    }

    [Fact]
    public void Create_WithEmptyName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => InstructionTemplate.Create(string.Empty, AgentRole.Coding, InstructionType.Guideline, "content"));
    }

    [Fact]
    public void Create_WithEmptyContent_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => InstructionTemplate.Create("Strict TDD", AgentRole.Coding, InstructionType.Guideline, string.Empty));
    }

    [Fact]
    public void Update_WithValidInput_UpdatesNameAndContent()
    {
        var template = InstructionTemplate.Create("Strict TDD", AgentRole.Coding, InstructionType.Guideline, "Write the test first.");

        template.Update("Strict TDD v2", "Write the test first, always.");

        Assert.Equal("Strict TDD v2", template.Name);
        Assert.Equal("Write the test first, always.", template.Content);
    }

    [Fact]
    public void Update_WithEmptyName_ThrowsArgumentException()
    {
        var template = InstructionTemplate.Create("Strict TDD", AgentRole.Coding, InstructionType.Guideline, "content");

        Assert.Throws<ArgumentException>(() => template.Update(string.Empty, "content"));
    }
}
