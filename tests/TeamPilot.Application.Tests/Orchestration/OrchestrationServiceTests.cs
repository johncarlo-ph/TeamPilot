using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TeamPilot.Application.Agents;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Git;
using TeamPilot.Application.Llm;
using TeamPilot.Application.Orchestration;
using TeamPilot.Application.Orchestration.Dtos;
using TeamPilot.Application.Orchestration.Validators;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Tickets;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using Xunit;

namespace TeamPilot.Application.Tests.Orchestration;

public class OrchestrationServiceTests
{
    private readonly Mock<ITicketRepository> _ticketRepository = new();
    private readonly Mock<IAgentRepository> _agentRepository = new();
    private readonly Mock<IProjectRepository> _projectRepository = new();
    private readonly Mock<IGitService> _gitService = new();
    private readonly Mock<IGitCredentialProtector> _credentialProtector = new();
    private readonly Mock<ILlmConnector> _llmConnector = new();
    private readonly Mock<IProjectAccessGuard> _projectAccessGuard = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly OrchestrationService _sut;
    private readonly Project _project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main");

    public OrchestrationServiceTests()
    {
        _projectRepository.Setup(r => r.GetByIdAsync(_project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_project);
        _credentialProtector.Setup(p => p.Unprotect(_project.EncryptedAccessToken)).Returns("plaintext-token");

        _sut = new OrchestrationService(
            _ticketRepository.Object,
            _agentRepository.Object,
            _projectRepository.Object,
            _gitService.Object,
            _credentialProtector.Object,
            _llmConnector.Object,
            _projectAccessGuard.Object,
            _unitOfWork.Object,
            new AssignSubAgentsRequestValidator(),
            NullLogger<OrchestrationService>.Instance);
    }

    [Fact]
    public async Task AssignSubAgentsAsync_WhenAgentsExist_AssignsAllAndMovesTicketToInProgress()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        var agent = Agent.Create(_project.Id, "Researcher", AgentRole.Research);

        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _agentRepository.Setup(r => r.GetByIdAsync(agent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agent);

        var result = await _sut.AssignSubAgentsAsync(ticket.Id, new AssignSubAgentsRequest([agent.Id]));

        Assert.Equal(TicketStatus.InProgress, result.Status);
        Assert.Single(ticket.Assignments);
    }

    [Fact]
    public async Task ExecuteAgentWorkAsync_WhenAgentIsResearch_DoesNotCreateCommit()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        var agent = Agent.Create(_project.Id, "Researcher", AgentRole.Research);

        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _agentRepository.Setup(r => r.GetByIdAsync(agent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agent);
        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse("Research notes", "claude-test", 10, 20));

        var result = await _sut.ExecuteAgentWorkAsync(ticket.Id, agent.Id);

        Assert.Null(result.Commit);
        Assert.Equal("Research notes", result.LlmOutput);
    }

    [Fact]
    public async Task ExecuteAgentWorkAsync_WhenAgentIsCodingWithLinkedBranch_CommitsToGitAndRecordsCommit()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        ticket.LinkBranch("feature/build-feature");
        var agent = Agent.Create(_project.Id, "Coder", AgentRole.Coding);

        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _agentRepository.Setup(r => r.GetByIdAsync(agent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agent);
        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse("Generated code", "claude-test", 10, 20));
        _gitService
            .Setup(g => g.CommitFileAsync(
                _project.RepositoryPath, "feature/build-feature", It.IsAny<string>(), "Generated code", It.IsAny<string>(), agent.Name, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommitResult("abc123", "diff content"));

        var result = await _sut.ExecuteAgentWorkAsync(ticket.Id, agent.Id);

        Assert.NotNull(result.Commit);
        Assert.Equal("abc123", result.Commit!.CommitHash);
        Assert.Single(ticket.Commits);
        _gitService.Verify(
            g => g.PushAsync(_project.RepositoryPath, "feature/build-feature", "plaintext-token", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAgentWorkAsync_WhenAgentIsCodingWithoutLinkedBranch_ThrowsInvalidOperationException()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        var agent = Agent.Create(_project.Id, "Coder", AgentRole.Coding);

        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _agentRepository.Setup(r => r.GetByIdAsync(agent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agent);
        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse("Generated code", "claude-test", 10, 20));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.ExecuteAgentWorkAsync(ticket.Id, agent.Id));
    }
}
