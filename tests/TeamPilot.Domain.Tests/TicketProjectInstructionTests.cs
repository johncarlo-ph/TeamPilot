using TeamPilot.Domain.Entities;
using Xunit;

namespace TeamPilot.Domain.Tests;

public class TicketProjectInstructionTests
{
    private static readonly Guid TicketId = Guid.NewGuid();
    private static readonly Guid AgentId = Guid.NewGuid();
    private static readonly Guid ReferencedProjectId = Guid.NewGuid();

    [Fact]
    public void Create_WithValidArguments_SetsAllProperties()
    {
        var instruction = TicketProjectInstruction.Create(TicketId, AgentId, ReferencedProjectId, "Add a new /health endpoint.");

        Assert.Equal(TicketId, instruction.TicketId);
        Assert.Equal(AgentId, instruction.AgentId);
        Assert.Equal(ReferencedProjectId, instruction.ReferencedProjectId);
        Assert.Equal("Add a new /health endpoint.", instruction.Text);
    }

    [Fact]
    public void Create_WithNullAgentId_Succeeds()
    {
        var instruction = TicketProjectInstruction.Create(TicketId, null, ReferencedProjectId, "Add a new /health endpoint.");

        Assert.Null(instruction.AgentId);
    }

    [Fact]
    public void Create_WithEmptyTicketId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => TicketProjectInstruction.Create(Guid.Empty, AgentId, ReferencedProjectId, "Add a new /health endpoint."));
    }

    [Fact]
    public void Create_WithEmptyReferencedProjectId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => TicketProjectInstruction.Create(TicketId, AgentId, Guid.Empty, "Add a new /health endpoint."));
    }

    [Fact]
    public void Create_WithWhitespaceText_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => TicketProjectInstruction.Create(TicketId, AgentId, ReferencedProjectId, "   "));
    }
}
