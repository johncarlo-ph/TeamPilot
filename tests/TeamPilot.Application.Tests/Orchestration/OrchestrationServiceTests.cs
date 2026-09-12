using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TeamPilot.Application.Agents;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Git;
using TeamPilot.Application.Instructions;
using TeamPilot.Application.Llm;
using TeamPilot.Application.Orchestration;
using TeamPilot.Application.Projects;
using TeamPilot.Application.Tickets;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using TeamPilot.Domain.Exceptions;
using Xunit;

namespace TeamPilot.Application.Tests.Orchestration;

public class OrchestrationServiceTests
{
    private readonly Mock<ITicketRepository> _ticketRepository = new();
    private readonly Mock<IAgentRepository> _agentRepository = new();
    private readonly Mock<IAgentService> _agentService = new();
    private readonly Mock<IProjectRepository> _projectRepository = new();
    private readonly Mock<IGitService> _gitService = new();
    private readonly Mock<IGitCredentialProtector> _credentialProtector = new();
    private readonly Mock<ILlmConnector> _llmConnector = new();
    private readonly Mock<IInstructionRepository> _instructionRepository = new();
    private readonly Mock<IProjectAccessGuard> _projectAccessGuard = new();
    private readonly Mock<IAuditLogger> _auditLogger = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly OrchestrationService _sut;
    private readonly Project _project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main");

    private readonly Agent _researchAgent;
    private readonly Agent _designAgent;
    private readonly Agent _codingAgent;
    private readonly Agent _testingAgent;

    public OrchestrationServiceTests()
    {
        _researchAgent = Agent.Create(_project.Id, "Research Agent", AgentRole.Research);
        _designAgent = Agent.Create(_project.Id, "Design Agent", AgentRole.Design);
        _codingAgent = Agent.Create(_project.Id, "Coding Agent", AgentRole.Coding);
        _testingAgent = Agent.Create(_project.Id, "Testing Agent", AgentRole.Testing);

        _projectRepository.Setup(r => r.GetByIdAsync(_project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_project);
        _credentialProtector.Setup(p => p.Unprotect(_project.EncryptedAccessToken)).Returns("plaintext-token");
        _agentService.Setup(s => s.EnsureDefaultAgentsAsync(_project.Id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _agentRepository.Setup(r => r.GetByProjectAndRoleAsync(_project.Id, AgentRole.Research, It.IsAny<CancellationToken>())).ReturnsAsync(_researchAgent);
        _agentRepository.Setup(r => r.GetByProjectAndRoleAsync(_project.Id, AgentRole.Design, It.IsAny<CancellationToken>())).ReturnsAsync(_designAgent);
        _agentRepository.Setup(r => r.GetByProjectAndRoleAsync(_project.Id, AgentRole.Coding, It.IsAny<CancellationToken>())).ReturnsAsync(_codingAgent);
        _agentRepository.Setup(r => r.GetByProjectAndRoleAsync(_project.Id, AgentRole.Testing, It.IsAny<CancellationToken>())).ReturnsAsync(_testingAgent);
        _gitService
            .Setup(g => g.CommitFileAsync(_project.RepositoryPath, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), _codingAgent.Name, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommitResult("abc123", "diff content"));

        _sut = new OrchestrationService(
            _ticketRepository.Object,
            _agentRepository.Object,
            _agentService.Object,
            _projectRepository.Object,
            _gitService.Object,
            _credentialProtector.Object,
            _llmConnector.Object,
            _instructionRepository.Object,
            _projectAccessGuard.Object,
            _auditLogger.Object,
            _unitOfWork.Object,
            NullLogger<OrchestrationService>.Instance);
    }

    private static bool IsTestingPrompt(LlmRequest request) => request.Prompt.Contains("You are the Testing agent");

    [Fact]
    public async Task RunPipelineAsync_HappyPath_AssignsAllFourAgentsLinksBranchAndMovesToReview()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Returns((LlmRequest req, CancellationToken _) => Task.FromResult(
                IsTestingPrompt(req)
                    ? new LlmResponse("All checks passed.\nRESULT: PASS", "claude-test", 10, 20)
                    : new LlmResponse("Some output", "claude-test", 10, 20)));

        var result = await _sut.RunPipelineAsync(ticket.Id);

        Assert.Equal(TicketStatus.ForReview, ticket.Status);
        Assert.Equal(4, ticket.Assignments.Count);
        Assert.Equal(ticket.Id.ToString("N")[..8], ticket.BranchName);
        Assert.Single(ticket.Commits);
        Assert.True(result.TestingPassed);
        Assert.Equal(1, result.TestingAttempts);
        _gitService.Verify(g => g.PushAsync(_project.RepositoryPath, ticket.BranchName!, "plaintext-token", It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task RunPipelineAsync_WhenTestingFailsThenPasses_RetriesCodingWithFeedbackAndSucceeds()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        var testingCallCount = 0;
        var codingPrompts = new List<string>();

        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Returns((LlmRequest req, CancellationToken _) =>
            {
                if (IsTestingPrompt(req))
                {
                    testingCallCount++;
                    return Task.FromResult(testingCallCount == 1
                        ? new LlmResponse("Found a bug.\nRESULT: FAIL", "claude-test", 10, 20)
                        : new LlmResponse("Looks good now.\nRESULT: PASS", "claude-test", 10, 20));
                }

                if (req.Prompt.Contains("You are the Coding agent"))
                {
                    codingPrompts.Add(req.Prompt);
                }

                return Task.FromResult(new LlmResponse("Some output", "claude-test", 10, 20));
            });

        var result = await _sut.RunPipelineAsync(ticket.Id);

        Assert.True(result.TestingPassed);
        Assert.Equal(2, result.TestingAttempts);
        Assert.Equal(2, ticket.Commits.Count);
        Assert.Equal(2, codingPrompts.Count);
        Assert.Contains("Found a bug", codingPrompts[1]);
        _gitService.Verify(
            g => g.CommitFileAsync(_project.RepositoryPath, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), _codingAgent.Name, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task RunPipelineAsync_WhenTestingFailsEveryAttempt_StopsAfterMaxAttemptsAndStillMovesToReview()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Returns((LlmRequest req, CancellationToken _) => Task.FromResult(
                IsTestingPrompt(req)
                    ? new LlmResponse("Still broken.\nRESULT: FAIL", "claude-test", 10, 20)
                    : new LlmResponse("Some output", "claude-test", 10, 20)));

        var result = await _sut.RunPipelineAsync(ticket.Id);

        Assert.False(result.TestingPassed);
        Assert.Equal(3, result.TestingAttempts);
        Assert.Equal(TicketStatus.ForReview, ticket.Status);
        Assert.Equal(3, ticket.Commits.Count);
    }

    [Fact]
    public async Task RunPipelineAsync_WhenPipelineAgentIsMissing_ThrowsInvalidOperationException()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _agentRepository
            .Setup(r => r.GetByProjectAndRoleAsync(_project.Id, AgentRole.Research, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agent?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.RunPipelineAsync(ticket.Id));
    }

    [Fact]
    public async Task RunPipelineAsync_IncludesEachAgentsCurrentInstructionsInItsPrompt()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        var codingConstitution = _codingAgent.AddInstructionVersion(InstructionType.Constitution, "Always write tests first.", "Admin");
        _instructionRepository
            .Setup(r => r.GetCurrentAsync(_codingAgent.Id, InstructionType.Constitution, It.IsAny<CancellationToken>()))
            .ReturnsAsync(codingConstitution);

        var codingPrompts = new List<string>();
        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Returns((LlmRequest req, CancellationToken _) =>
            {
                if (IsTestingPrompt(req))
                {
                    return Task.FromResult(new LlmResponse("All good.\nRESULT: PASS", "claude-test", 10, 20));
                }

                if (req.Prompt.Contains("You are the Coding agent"))
                {
                    codingPrompts.Add(req.Prompt);
                }

                return Task.FromResult(new LlmResponse("Some output", "claude-test", 10, 20));
            });

        await _sut.RunPipelineAsync(ticket.Id);

        Assert.Single(codingPrompts);
        Assert.Contains("Standing instructions for this agent:", codingPrompts[0]);
        Assert.Contains("Always write tests first.", codingPrompts[0]);
    }

    [Fact]
    public async Task RunPipelineAsync_WhenInstructionsChangeMidRun_CodingRetryUsesTheUpdatedInstructions()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        Instruction? currentInstruction = _codingAgent.AddInstructionVersion(InstructionType.Guideline, "Original guideline.", "Admin");
        _instructionRepository
            .Setup(r => r.GetCurrentAsync(_codingAgent.Id, InstructionType.Guideline, It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult<Instruction?>(currentInstruction));

        var testingCallCount = 0;
        var codingPrompts = new List<string>();

        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Returns((LlmRequest req, CancellationToken _) =>
            {
                if (IsTestingPrompt(req))
                {
                    testingCallCount++;
                    if (testingCallCount == 1)
                    {
                        // Simulate an admin editing the Coding agent's instructions while this
                        // ticket's pipeline is still mid-run - between the first Coding/Testing
                        // attempt and the Coding retry that follows the Testing failure.
                        currentInstruction = _codingAgent.AddInstructionVersion(InstructionType.Guideline, "Updated guideline after admin edit.", "Admin");
                        return Task.FromResult(new LlmResponse("Found a bug.\nRESULT: FAIL", "claude-test", 10, 20));
                    }

                    return Task.FromResult(new LlmResponse("Looks good now.\nRESULT: PASS", "claude-test", 10, 20));
                }

                if (req.Prompt.Contains("You are the Coding agent"))
                {
                    codingPrompts.Add(req.Prompt);
                }

                return Task.FromResult(new LlmResponse("Some output", "claude-test", 10, 20));
            });

        await _sut.RunPipelineAsync(ticket.Id);

        Assert.Equal(2, codingPrompts.Count);
        Assert.Contains("Original guideline.", codingPrompts[0]);
        Assert.DoesNotContain("Updated guideline", codingPrompts[0]);
        Assert.Contains("Updated guideline after admin edit.", codingPrompts[1]);
    }

    [Fact]
    public async Task RunPipelineAsync_WhenTicketIsCancelledMidRun_StopsBeforeTheNextStageAndThrows()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        var cancelled = false;
        _ticketRepository
            .Setup(r => r.GetStatusAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => cancelled ? TicketStatus.Cancelled : TicketStatus.InProgress);

        var codingCalls = 0;
        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Returns((LlmRequest req, CancellationToken _) =>
            {
                if (req.Prompt.Contains("You are the Design agent"))
                {
                    // Simulate someone cancelling the ticket while the Design stage's LLM call
                    // is still in flight - the pipeline can't interrupt that call, but the next
                    // stage (Coding) should refuse to start once it notices.
                    cancelled = true;
                }

                if (req.Prompt.Contains("You are the Coding agent"))
                {
                    codingCalls++;
                }

                return Task.FromResult(new LlmResponse("Some output", "claude-test", 10, 20));
            });

        await Assert.ThrowsAsync<InvalidTicketStateTransitionException>(() => _sut.RunPipelineAsync(ticket.Id));

        Assert.Equal(0, codingCalls);
        Assert.Empty(ticket.Commits);
    }
}
