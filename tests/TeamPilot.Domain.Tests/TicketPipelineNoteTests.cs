using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using Xunit;

namespace TeamPilot.Domain.Tests;

public class TicketPipelineNoteTests
{
    private static readonly Guid TicketId = Guid.NewGuid();
    private static readonly Guid AgentId = Guid.NewGuid();

    [Fact]
    public void Create_WithValidArguments_SetsAllProperties()
    {
        var note = TicketPipelineNote.Create(TicketId, AgentId, AgentRole.Testing, "Skipped the slow integration suite.");

        Assert.Equal(TicketId, note.TicketId);
        Assert.Equal(AgentId, note.AgentId);
        Assert.Equal(AgentRole.Testing, note.Role);
        Assert.Equal("Skipped the slow integration suite.", note.Text);
    }

    [Fact]
    public void Create_WithNullAgentIdAndRole_Succeeds()
    {
        var note = TicketPipelineNote.Create(TicketId, null, null, "Skipped the slow integration suite.");

        Assert.Null(note.AgentId);
        Assert.Null(note.Role);
    }

    [Fact]
    public void Create_WithEmptyTicketId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => TicketPipelineNote.Create(Guid.Empty, AgentId, AgentRole.Testing, "Skipped the slow integration suite."));
    }

    [Fact]
    public void Create_WithWhitespaceText_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => TicketPipelineNote.Create(TicketId, AgentId, AgentRole.Testing, "   "));
    }
}
