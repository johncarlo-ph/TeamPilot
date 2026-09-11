using Moq;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Conflicts;
using TeamPilot.Application.Conflicts.Dtos;
using TeamPilot.Application.Conflicts.Validators;
using TeamPilot.Application.Git;
using TeamPilot.Application.Llm;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Tickets;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using Xunit;

namespace TeamPilot.Application.Tests.Conflicts;

public class ConflictResolutionServiceTests
{
    private readonly Mock<ITicketRepository> _ticketRepository = new();
    private readonly Mock<IProjectRepository> _projectRepository = new();
    private readonly Mock<IConflictRepository> _conflictRepository = new();
    private readonly Mock<IGitService> _gitService = new();
    private readonly Mock<ILlmConnector> _llmConnector = new();
    private readonly Mock<IProjectAccessGuard> _projectAccessGuard = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly ConflictResolutionService _sut;
    private readonly Project _project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main");

    public ConflictResolutionServiceTests()
    {
        _projectRepository.Setup(r => r.GetByIdAsync(_project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_project);

        _sut = new ConflictResolutionService(
            _ticketRepository.Object,
            _projectRepository.Object,
            _conflictRepository.Object,
            _gitService.Object,
            _llmConnector.Object,
            _projectAccessGuard.Object,
            _unitOfWork.Object,
            new ResolveConflictManuallyRequestValidator(),
            new AcceptAiSuggestionRequestValidator());
    }

    private Ticket CreateTicket()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        return ticket;
    }

    [Fact]
    public async Task DetectConflictsAsync_WhenGitReportsConflicts_PersistsConflictsAgainstTicket()
    {
        var ticket = CreateTicket();
        ticket.LinkBranch("feature/build-feature");

        _gitService
            .Setup(g => g.DetectMergeConflictsAsync(_project.RepositoryPath, "feature/build-feature", "main", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitMergeConflictResult(true, [new GitConflictingFile("src/App.cs", "<<<<<<<")]));

        var result = await _sut.DetectConflictsAsync(ticket.Id);

        Assert.Single(result);
        Assert.Single(ticket.Conflicts);
    }

    [Fact]
    public async Task SuggestResolutionAsync_WhenLlmReturnsSuggestion_UpdatesConflictWithAiSuggestion()
    {
        var ticket = CreateTicket();
        var conflict = Conflict.Create(ticket.Id, "src/App.cs", "<<<<<<< conflict >>>>>>>");
        _conflictRepository.Setup(r => r.GetByIdAsync(conflict.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conflict);
        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse("Use theirs", "claude-test", 5, 5));

        var result = await _sut.SuggestResolutionAsync(conflict.Id);

        Assert.Equal("Use theirs", result.AiSuggestedResolution);
        Assert.Equal(ConflictStatus.AiResolutionSuggested, result.Status);
    }

    [Fact]
    public async Task AcceptAiSuggestionAsync_WhenNoSuggestionRecorded_ThrowsInvalidOperationException()
    {
        var ticket = CreateTicket();
        var conflict = Conflict.Create(ticket.Id, "src/App.cs", "<<<<<<< conflict >>>>>>>");
        _conflictRepository.Setup(r => r.GetByIdAsync(conflict.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conflict);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AcceptAiSuggestionAsync(conflict.Id, new AcceptAiSuggestionRequest("Alice")));
    }

    [Fact]
    public async Task ResolveManuallyAsync_WhenConflictIsDetected_SetsStatusToResolvedManually()
    {
        var ticket = CreateTicket();
        var conflict = Conflict.Create(ticket.Id, "src/App.cs", "<<<<<<< conflict >>>>>>>");
        _conflictRepository.Setup(r => r.GetByIdAsync(conflict.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conflict);

        var result = await _sut.ResolveManuallyAsync(conflict.Id, new ResolveConflictManuallyRequest("Kept ours", "Alice"));

        Assert.Equal(ConflictStatus.ResolvedManually, result.Status);
        Assert.Equal("Kept ours", result.ResolutionNote);
    }
}
