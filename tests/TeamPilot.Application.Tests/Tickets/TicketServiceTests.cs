using Moq;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Git;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Tickets;
using TeamPilot.Application.Tickets.Dtos;
using TeamPilot.Application.Tickets.Validators;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using Xunit;

namespace TeamPilot.Application.Tests.Tickets;

public class TicketServiceTests
{
    private readonly Mock<ITicketRepository> _ticketRepository = new();
    private readonly Mock<IProjectRepository> _projectRepository = new();
    private readonly Mock<IGitService> _gitService = new();
    private readonly Mock<IProjectAccessGuard> _projectAccessGuard = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly TicketService _sut;
    private readonly Project _project = Project.Create("TeamPilot", "desc", "C:/repos/teampilot");

    public TicketServiceTests()
    {
        _projectRepository.Setup(r => r.GetByIdAsync(_project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_project);

        _sut = new TicketService(
            _ticketRepository.Object,
            _projectRepository.Object,
            _gitService.Object,
            _projectAccessGuard.Object,
            _unitOfWork.Object,
            new CreateTicketRequestValidator(),
            new CreateBranchRequestValidator());
    }

    [Fact]
    public async Task CreateAsync_WithValidRequest_AddsTicketAndSavesChanges()
    {
        var request = new CreateTicketRequest("Implement login", "Add OAuth login flow");

        var result = await _sut.CreateAsync(_project.Id, request);

        Assert.Equal("Implement login", result.Title);
        Assert.Equal(_project.Id, result.ProjectId);
        Assert.Equal(TicketStatus.ToDo, result.Status);
        _ticketRepository.Verify(r => r.AddAsync(It.IsAny<Ticket>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithEmptyTitle_ThrowsValidationException()
    {
        var request = new CreateTicketRequest(string.Empty, null);

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateAsync(_project.Id, request));
    }

    [Fact]
    public async Task CreateAsync_WhenProjectDoesNotExist_ThrowsNotFoundException()
    {
        _projectRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Project?)null);
        var request = new CreateTicketRequest("Implement login", null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.CreateAsync(Guid.NewGuid(), request));
    }

    [Fact]
    public async Task GetByIdAsync_WhenTicketDoesNotExist_ThrowsNotFoundException()
    {
        _ticketRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Ticket?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task MoveToReviewAsync_WhenTicketIsInProgress_SetsStatusToForReview()
    {
        var ticket = Ticket.Create(_project.Id, "Fix bug", "desc");
        ticket.AssignAgent(Agent.Create(_project.Id, "Coder", AgentRole.Coding));

        _ticketRepository
            .Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        var result = await _sut.MoveToReviewAsync(ticket.Id);

        Assert.Equal(TicketStatus.ForReview, result.Status);
    }

    [Fact]
    public async Task LinkBranchAsync_WithValidBranchName_EnsuresBranchAndLinksTicket()
    {
        var ticket = Ticket.Create(_project.Id, "Fix bug", "desc");

        _ticketRepository
            .Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        var result = await _sut.LinkBranchAsync(ticket.Id, "feature/fix-bug");

        Assert.Equal("feature/fix-bug", result.BranchName);
        _gitService.Verify(g => g.EnsureBranchAsync(_project.RepositoryPath, "feature/fix-bug", It.IsAny<CancellationToken>()), Times.Once);
    }
}
