using TeamPilot.Domain.Entities;
using Xunit;

namespace TeamPilot.Domain.Tests;

public class StageExecutionTests
{
    private static readonly Guid TicketId = Guid.NewGuid();
    private static readonly Guid AgentId = Guid.NewGuid();

    [Fact]
    public void Create_WithEmptyTicketId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => StageExecution.Create(Guid.Empty, AgentId, "output"));
    }

    [Fact]
    public void Create_WithEmptyAgentId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => StageExecution.Create(TicketId, Guid.Empty, "output"));
    }

    [Fact]
    public void Create_WithValidArguments_SetsFields()
    {
        var execution = StageExecution.Create(TicketId, AgentId, "Findings go here.");

        Assert.Equal(TicketId, execution.TicketId);
        Assert.Equal(AgentId, execution.AgentId);
        Assert.Equal("Findings go here.", execution.Output);
    }
}
