using Moq;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.TicketAgentEvents;
using TeamPilot.Application.Tickets;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using Xunit;

namespace TeamPilot.Application.Tests.TicketAgentEvents;

public class TicketAgentEventServiceTests
{
    private readonly Mock<ITicketRepository> _ticketRepository = new();
    private readonly Mock<ITicketAgentEventRepository> _ticketAgentEventRepository = new();
    private readonly Mock<IProjectAccessGuard> _projectAccessGuard = new();
    private readonly TicketAgentEventService _sut;

    public TicketAgentEventServiceTests()
    {
        _sut = new TicketAgentEventService(
            _ticketRepository.Object,
            _ticketAgentEventRepository.Object,
            _projectAccessGuard.Object);
    }

    [Fact]
    public async Task ListByTicketAsync_ChecksProjectAccessAndReturnsTheRepositoryListing()
    {
        var ticket = Ticket.Create(Guid.NewGuid(), "Build feature", "desc");
        var events = new List<TicketAgentEvent> { TicketAgentEvent.CreateStarted(ticket.Id, Guid.NewGuid(), AgentRole.Research) };
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _ticketAgentEventRepository.Setup(r => r.ListByTicketAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(events);

        var result = await _sut.ListByTicketAsync(ticket.Id);

        Assert.Same(events, result);
        _projectAccessGuard.Verify(g => g.EnsureAccessAsync(ticket.ProjectId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListByTicketAsync_WhenTicketDoesNotExist_ThrowsNotFoundException()
    {
        var ticketId = Guid.NewGuid();
        _ticketRepository.Setup(r => r.GetByIdAsync(ticketId, It.IsAny<CancellationToken>())).ReturnsAsync((Ticket?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ListByTicketAsync(ticketId));
    }

    [Fact]
    public async Task ListByTicketAsync_WhenCallerLacksProjectAccess_ThrowsForbiddenExceptionAndNeverQueriesEvents()
    {
        var ticket = Ticket.Create(Guid.NewGuid(), "Build feature", "desc");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _projectAccessGuard.Setup(g => g.EnsureAccessAsync(ticket.ProjectId, It.IsAny<CancellationToken>())).ThrowsAsync(new ForbiddenException("No access."));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ListByTicketAsync(ticket.Id));
        _ticketAgentEventRepository.Verify(r => r.ListByTicketAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
