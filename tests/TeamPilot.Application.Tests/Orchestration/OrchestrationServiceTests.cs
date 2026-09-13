using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TeamPilot.Application.Agents;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Git;
using TeamPilot.Application.Instructions;
using TeamPilot.Application.Llm;
using TeamPilot.Application.Orchestration;
using TeamPilot.Application.Projects;
using TeamPilot.Application.TicketQuestions;
using TeamPilot.Application.Tickets;
using TeamPilot.Application.Workflow;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using TeamPilot.Domain.Exceptions;
using Xunit;

namespace TeamPilot.Application.Tests.Orchestration;

public class OrchestrationServiceTests
{
    private readonly Mock<ITicketRepository> _ticketRepository = new();
    private readonly Mock<IWorkflowStageRepository> _workflowStageRepository = new();
    private readonly Mock<IAgentRepository> _agentRepository = new();
    private readonly Mock<IWorkflowService> _workflowService = new();
    private readonly Mock<IProjectRepository> _projectRepository = new();
    private readonly Mock<IGitService> _gitService = new();
    private readonly Mock<IGitCredentialProtector> _credentialProtector = new();
    private readonly Mock<ILlmConnector> _llmConnector = new();
    private readonly Mock<IInstructionRepository> _instructionRepository = new();
    private readonly Mock<IStageExecutionRepository> _stageExecutionRepository = new();
    private readonly Mock<ITicketQuestionRepository> _ticketQuestionRepository = new();
    private readonly Mock<IProjectAccessGuard> _projectAccessGuard = new();
    private readonly Mock<IAuditLogger> _auditLogger = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly OrchestrationService _sut;
    private readonly Project _project = Project.Create("TeamPilot", "desc", "https://github.com/org/teampilot.git", "encrypted-token", "main");

    private readonly Agent _researchAgent;
    private readonly Agent _designAgent;
    private readonly Agent _codingAgent;
    private readonly Agent _testingAgent;

    private readonly WorkflowStage _researchStage;
    private readonly WorkflowStage _designStage;
    private readonly WorkflowStage _codingStage;
    private readonly WorkflowStage _testingStage;

    public OrchestrationServiceTests()
    {
        _researchAgent = Agent.Create(_project.Id, "Research Agent", AgentRole.Research);
        _designAgent = Agent.Create(_project.Id, "Design Agent", AgentRole.Design);
        _codingAgent = Agent.Create(_project.Id, "Coding Agent", AgentRole.Coding);
        _testingAgent = Agent.Create(_project.Id, "Testing Agent", AgentRole.Testing);

        _researchStage = WorkflowStage.Create(_project.Id, _researchAgent.Id, 0);
        _designStage = WorkflowStage.Create(_project.Id, _designAgent.Id, 1);
        _codingStage = WorkflowStage.Create(_project.Id, _codingAgent.Id, 2);
        _testingStage = WorkflowStage.Create(_project.Id, _testingAgent.Id, 3);
        _testingStage.SetLoopBack(_codingStage.Id, 3);

        _projectRepository.Setup(r => r.GetByIdAsync(_project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_project);
        _credentialProtector.Setup(p => p.Unprotect(_project.EncryptedAccessToken)).Returns("plaintext-token");
        _workflowService.Setup(s => s.EnsureDefaultWorkflowAsync(_project.Id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        SetUpStages(_researchStage, _designStage, _codingStage, _testingStage);
        SetUpAgents(_researchAgent, _designAgent, _codingAgent, _testingAgent);

        _stageExecutionRepository
            .Setup(r => r.GetLatestByTicketAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, StageExecution>());

        _ticketQuestionRepository
            .Setup(r => r.GetMostRecentUnconsumedAnsweredAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TicketQuestion?)null);

        _gitService
            .Setup(g => g.CommitFilesAsync(_project.RepositoryPath, It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), _codingAgent.Name, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommitResult("abc123", "diff content"));

        _gitService
            .Setup(g => g.GetRepositorySnapshotAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        _sut = new OrchestrationService(
            _ticketRepository.Object,
            _workflowStageRepository.Object,
            _agentRepository.Object,
            _workflowService.Object,
            _projectRepository.Object,
            _gitService.Object,
            _credentialProtector.Object,
            _llmConnector.Object,
            _instructionRepository.Object,
            _stageExecutionRepository.Object,
            _ticketQuestionRepository.Object,
            _projectAccessGuard.Object,
            _auditLogger.Object,
            _unitOfWork.Object,
            NullLogger<OrchestrationService>.Instance);
    }

    private void SetUpStages(params WorkflowStage[] stages) =>
        _workflowStageRepository.Setup(r => r.ListOrderedAsync(_project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stages);

    private void SetUpAgents(params Agent[] agents) =>
        _agentRepository.Setup(r => r.ListAsync(_project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agents);

    private static bool IsPromptFor(LlmRequest request, Agent agent) => request.Prompt.Contains($"You are {agent.Name} (");

    /// <summary>A Coding-stage response with a parseable &lt;file&gt; block - since a commit now
    /// only happens when at least one such block parses (see OrchestrationService.ParseFileChanges),
    /// tests that assert a commit occurred use this instead of a plain narrative string.</summary>
    private static string CodingOutputWithFileChange(string narrative = "Implemented the change.") =>
        $"{narrative}\n<file path=\"src/App.tsx\">\nexport const App = () => <div>Hi</div>;\n</file>";

    [Fact]
    public async Task RunPipelineAsync_HappyPath_AssignsAllFourAgentsLinksBranchAndMovesToReview()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Returns((LlmRequest req, CancellationToken _) => Task.FromResult(
                IsPromptFor(req, _testingAgent)
                    ? new LlmResponse("All checks passed.\nRESULT: PASS", "claude-test", 10, 20)
                    : IsPromptFor(req, _codingAgent)
                        ? new LlmResponse(CodingOutputWithFileChange(), "claude-test", 10, 20)
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
                if (IsPromptFor(req, _testingAgent))
                {
                    testingCallCount++;
                    return Task.FromResult(testingCallCount == 1
                        ? new LlmResponse("Found a bug.\nRESULT: FAIL", "claude-test", 10, 20)
                        : new LlmResponse("Looks good now.\nRESULT: PASS", "claude-test", 10, 20));
                }

                if (IsPromptFor(req, _codingAgent))
                {
                    codingPrompts.Add(req.Prompt);
                    return Task.FromResult(new LlmResponse(CodingOutputWithFileChange(), "claude-test", 10, 20));
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
            g => g.CommitFilesAsync(_project.RepositoryPath, It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), _codingAgent.Name, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task RunPipelineAsync_WhenTestingFailsEveryAttempt_StopsAfterMaxIterationsAndStillMovesToReview()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Returns((LlmRequest req, CancellationToken _) => Task.FromResult(
                IsPromptFor(req, _testingAgent)
                    ? new LlmResponse("Still broken.\nRESULT: FAIL", "claude-test", 10, 20)
                    : IsPromptFor(req, _codingAgent)
                        ? new LlmResponse(CodingOutputWithFileChange(), "claude-test", 10, 20)
                        : new LlmResponse("Some output", "claude-test", 10, 20)));

        var result = await _sut.RunPipelineAsync(ticket.Id);

        Assert.False(result.TestingPassed);
        Assert.Equal(3, result.TestingAttempts);
        Assert.Equal(TicketStatus.ForReview, ticket.Status);
        Assert.Equal(3, ticket.Commits.Count);
    }

    [Fact]
    public async Task RunPipelineAsync_WhenProjectHasNoWorkflowStages_ThrowsInvalidOperationException()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _workflowStageRepository
            .Setup(r => r.ListOrderedAsync(_project.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.RunPipelineAsync(ticket.Id));
    }

    [Fact]
    public async Task RunPipelineAsync_IncludesEachAgentsCurrentInstructionsInItsPrompt()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        var codingGuideline = _codingAgent.AddInstructionVersion(InstructionType.Guideline, "Always write tests first.", "Admin");
        _instructionRepository
            .Setup(r => r.GetCurrentAsync(_codingAgent.Id, InstructionType.Guideline, It.IsAny<CancellationToken>()))
            .ReturnsAsync(codingGuideline);

        var codingPrompts = new List<string>();
        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Returns((LlmRequest req, CancellationToken _) =>
            {
                if (IsPromptFor(req, _testingAgent))
                {
                    return Task.FromResult(new LlmResponse("All good.\nRESULT: PASS", "claude-test", 10, 20));
                }

                if (IsPromptFor(req, _codingAgent))
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
                if (IsPromptFor(req, _testingAgent))
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

                if (IsPromptFor(req, _codingAgent))
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
                if (IsPromptFor(req, _designAgent))
                {
                    // Simulate someone cancelling the ticket while the Design stage's LLM call
                    // is still in flight - the pipeline can't interrupt that call, but the next
                    // stage (Coding) should refuse to start once it notices.
                    cancelled = true;
                }

                if (IsPromptFor(req, _codingAgent))
                {
                    codingCalls++;
                }

                return Task.FromResult(new LlmResponse("Some output", "claude-test", 10, 20));
            });

        await Assert.ThrowsAsync<InvalidTicketStateTransitionException>(() => _sut.RunPipelineAsync(ticket.Id));

        Assert.Equal(0, codingCalls);
        Assert.Empty(ticket.Commits);
    }

    [Fact]
    public async Task RunPipelineAsync_WhenTicketIsCancelledDuringCodingsOwnLlmCall_SkipsTheCommit()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        var cancelled = false;
        _ticketRepository
            .Setup(r => r.GetStatusAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => cancelled ? TicketStatus.Cancelled : TicketStatus.InProgress);

        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Returns((LlmRequest req, CancellationToken _) =>
            {
                if (IsPromptFor(req, _codingAgent))
                {
                    // Unlike the Design-stage scenario above, this simulates a concurrent
                    // cancellation landing while Coding's OWN LLM call is in flight - the one
                    // gap RunCodingStageAsync can't preempt. Its own fresh status re-check,
                    // taken right before committing (under the same GitRepositoryLock a
                    // concurrent TicketService.CancelAsync uses for its branch delete), should
                    // still catch this and skip the commit rather than let it land - and
                    // possibly resurrect a branch that cancellation already deleted.
                    cancelled = true;
                    return Task.FromResult(new LlmResponse(CodingOutputWithFileChange(), "claude-test", 10, 20));
                }

                return Task.FromResult(new LlmResponse("Some output", "claude-test", 10, 20));
            });

        await Assert.ThrowsAsync<InvalidTicketStateTransitionException>(() => _sut.RunPipelineAsync(ticket.Id));

        Assert.Empty(ticket.Commits);
        _gitService.Verify(
            g => g.CommitFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunPipelineAsync_WithReorderedStages_ExecutesInConfiguredOrderNotDefaultRoleOrder()
    {
        // Swap Research and Design's positions - the workflow should run Design before Research,
        // proving that WorkflowStage.Order (not a hardcoded role order) drives execution.
        var swappedDesignStage = WorkflowStage.Create(_project.Id, _designAgent.Id, 0);
        var swappedResearchStage = WorkflowStage.Create(_project.Id, _researchAgent.Id, 1);
        SetUpStages(swappedDesignStage, swappedResearchStage, _codingStage, _testingStage);

        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        var executionOrder = new List<AgentRole>();
        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Returns((LlmRequest req, CancellationToken _) =>
            {
                if (IsPromptFor(req, _researchAgent))
                {
                    executionOrder.Add(AgentRole.Research);
                }
                else if (IsPromptFor(req, _designAgent))
                {
                    executionOrder.Add(AgentRole.Design);
                }

                return Task.FromResult(IsPromptFor(req, _testingAgent)
                    ? new LlmResponse("RESULT: PASS", "claude-test", 10, 20)
                    : new LlmResponse("Some output", "claude-test", 10, 20));
            });

        await _sut.RunPipelineAsync(ticket.Id);

        Assert.Equal([AgentRole.Design, AgentRole.Research], executionOrder);
    }

    [Fact]
    public async Task RunPipelineAsync_WithCustomPromptOnlyAgent_NeverCommitsToGit()
    {
        var customAgent = Agent.Create(_project.Id, "Reviewer Agent", AgentRole.Custom);
        customAgent.AddInstructionVersion(InstructionType.Constitution, "c", "admin");
        customAgent.AddInstructionVersion(InstructionType.Guideline, "g", "admin");
        customAgent.AddInstructionVersion(InstructionType.Requirement, "r", "admin");
        var customStage = WorkflowStage.Create(_project.Id, customAgent.Id, 4);

        SetUpStages(_researchStage, _designStage, _codingStage, _testingStage, customStage);
        SetUpAgents(_researchAgent, _designAgent, _codingAgent, _testingAgent, customAgent);

        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Returns((LlmRequest req, CancellationToken _) => Task.FromResult(
                IsPromptFor(req, _testingAgent)
                    ? new LlmResponse("RESULT: PASS", "claude-test", 10, 20)
                    : IsPromptFor(req, _codingAgent)
                        ? new LlmResponse(CodingOutputWithFileChange(), "claude-test", 10, 20)
                        : new LlmResponse("Some output", "claude-test", 10, 20)));

        await _sut.RunPipelineAsync(ticket.Id);

        Assert.Single(ticket.Commits);
        _gitService.Verify(
            g => g.CommitFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), customAgent.Name, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunPipelineAsync_WhenTicketHasAPriorRequestChangesReview_IncludesItsCommentsInEveryStagePrompt()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        ticket.RecordReview(Review.Create(ticket.Id, "Bob", ReviewDecision.RequestChanges, "The null check on line 12 is missing"));
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        var researchPrompts = new List<string>();
        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Returns((LlmRequest req, CancellationToken _) =>
            {
                if (IsPromptFor(req, _researchAgent))
                {
                    researchPrompts.Add(req.Prompt);
                }

                return Task.FromResult(IsPromptFor(req, _testingAgent)
                    ? new LlmResponse("RESULT: PASS", "claude-test", 10, 20)
                    : new LlmResponse("Some output", "claude-test", 10, 20));
            });

        await _sut.RunPipelineAsync(ticket.Id);

        Assert.Single(researchPrompts);
        Assert.Contains("The null check on line 12 is missing", researchPrompts[0]);
    }

    [Fact]
    public async Task RunPipelineAsync_WhenTicketHasOnlyAnOlderApprovedReview_DoesNotIncludeReviewFeedback()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        ticket.RecordReview(Review.Create(ticket.Id, "Bob", ReviewDecision.Approve, "Looks good"));
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        var researchPrompts = new List<string>();
        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Returns((LlmRequest req, CancellationToken _) =>
            {
                if (IsPromptFor(req, _researchAgent))
                {
                    researchPrompts.Add(req.Prompt);
                }

                return Task.FromResult(IsPromptFor(req, _testingAgent)
                    ? new LlmResponse("RESULT: PASS", "claude-test", 10, 20)
                    : new LlmResponse("Some output", "claude-test", 10, 20));
            });

        await _sut.RunPipelineAsync(ticket.Id);

        Assert.Single(researchPrompts);
        Assert.DoesNotContain("Looks good", researchPrompts[0]);
        Assert.DoesNotContain("reviewer", researchPrompts[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunPipelineAsync_WhenAgentHasAPriorStageExecution_GivesItItsOwnPriorOutputAlongsideReviewFeedback()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        ticket.RecordReview(Review.Create(ticket.Id, "Bob", ReviewDecision.RequestChanges, "Needs work"));
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _stageExecutionRepository
            .Setup(r => r.GetLatestByTicketAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, StageExecution>
            {
                [_researchAgent.Id] = StageExecution.Create(ticket.Id, _researchAgent.Id, "Original research findings."),
            });

        var researchPrompts = new List<string>();
        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Returns((LlmRequest req, CancellationToken _) =>
            {
                if (IsPromptFor(req, _researchAgent))
                {
                    researchPrompts.Add(req.Prompt);
                }

                return Task.FromResult(IsPromptFor(req, _testingAgent)
                    ? new LlmResponse("RESULT: PASS", "claude-test", 10, 20)
                    : new LlmResponse("Some output", "claude-test", 10, 20));
            });

        await _sut.RunPipelineAsync(ticket.Id);

        Assert.Single(researchPrompts);
        Assert.Contains("Needs work", researchPrompts[0]);
        Assert.Contains("Original research findings.", researchPrompts[0]);
        Assert.Contains("reaffirm", researchPrompts[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunPipelineAsync_WhenAgentHasNoPriorStageExecution_DoesNotMentionReaffirmingEvenWithReviewFeedback()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        ticket.RecordReview(Review.Create(ticket.Id, "Bob", ReviewDecision.RequestChanges, "Needs work"));
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        // No entries in the dictionary at all - the default constructor setup already covers this,
        // spelled out here for clarity.
        _stageExecutionRepository
            .Setup(r => r.GetLatestByTicketAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, StageExecution>());

        var researchPrompts = new List<string>();
        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Returns((LlmRequest req, CancellationToken _) =>
            {
                if (IsPromptFor(req, _researchAgent))
                {
                    researchPrompts.Add(req.Prompt);
                }

                return Task.FromResult(IsPromptFor(req, _testingAgent)
                    ? new LlmResponse("RESULT: PASS", "claude-test", 10, 20)
                    : new LlmResponse("Some output", "claude-test", 10, 20));
            });

        await _sut.RunPipelineAsync(ticket.Id);

        Assert.Single(researchPrompts);
        Assert.Contains("Needs work", researchPrompts[0]);
        Assert.DoesNotContain("reaffirm", researchPrompts[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("own previous output", researchPrompts[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunPipelineAsync_WhenCodingIsRevisitedViaIntraRunLoopBack_DoesNotMixInThePriorRunOutputFraming()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        ticket.RecordReview(Review.Create(ticket.Id, "Bob", ReviewDecision.RequestChanges, "Needs work"));
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _stageExecutionRepository
            .Setup(r => r.GetLatestByTicketAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, StageExecution>
            {
                [_codingAgent.Id] = StageExecution.Create(ticket.Id, _codingAgent.Id, "Original implementation notes."),
            });

        var testingCallCount = 0;
        var codingPrompts = new List<string>();
        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Returns((LlmRequest req, CancellationToken _) =>
            {
                if (IsPromptFor(req, _codingAgent))
                {
                    codingPrompts.Add(req.Prompt);
                }

                if (IsPromptFor(req, _testingAgent))
                {
                    testingCallCount++;
                    return Task.FromResult(testingCallCount == 1
                        ? new LlmResponse("Found a bug.\nRESULT: FAIL", "claude-test", 10, 20)
                        : new LlmResponse("Looks good now.\nRESULT: PASS", "claude-test", 10, 20));
                }

                return Task.FromResult(new LlmResponse("Some output", "claude-test", 10, 20));
            });

        await _sut.RunPipelineAsync(ticket.Id);

        Assert.Equal(2, codingPrompts.Count);
        Assert.Contains("Original implementation notes.", codingPrompts[0]);
        Assert.Contains("reaffirm", codingPrompts[0], StringComparison.OrdinalIgnoreCase);
        // The retry (attempt 2, via the intra-run Testing loop-back) uses the ordinary
        // previousOutput-carries-forward mechanism, not the prior-run reaffirm/revise framing.
        Assert.DoesNotContain("Original implementation notes.", codingPrompts[1]);
        Assert.DoesNotContain("reaffirm", codingPrompts[1], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Found a bug", codingPrompts[1]);
    }

    [Fact]
    public async Task RunPipelineAsync_WhenCodingReportsNoChangesOnAReviewRerun_SkipsTheCommitButStillRecordsAStageExecution()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        ticket.RecordReview(Review.Create(ticket.Id, "Bob", ReviewDecision.RequestChanges, "Actually, never mind"));
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _stageExecutionRepository
            .Setup(r => r.GetLatestByTicketAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, StageExecution>
            {
                [_codingAgent.Id] = StageExecution.Create(ticket.Id, _codingAgent.Id, "Original implementation notes."),
            });

        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Returns((LlmRequest req, CancellationToken _) => Task.FromResult(
                IsPromptFor(req, _codingAgent)
                    ? new LlmResponse("No change is needed here.\nCHANGES: NONE", "claude-test", 10, 20)
                    : IsPromptFor(req, _testingAgent)
                        ? new LlmResponse("RESULT: PASS", "claude-test", 10, 20)
                        : new LlmResponse("Some output", "claude-test", 10, 20)));

        await _sut.RunPipelineAsync(ticket.Id);

        Assert.Empty(ticket.Commits);
        _gitService.Verify(
            g => g.CommitFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _stageExecutionRepository.Verify(
            r => r.AddAsync(It.Is<StageExecution>(e => e.AgentId == _codingAgent.Id && e.Output.Contains("No change is needed")), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunPipelineAsync_WhenCodingReportsChangesMadeOnAReviewRerun_CommitsAsUsual()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        ticket.RecordReview(Review.Create(ticket.Id, "Bob", ReviewDecision.RequestChanges, "Fix it"));
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _stageExecutionRepository
            .Setup(r => r.GetLatestByTicketAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, StageExecution>
            {
                [_codingAgent.Id] = StageExecution.Create(ticket.Id, _codingAgent.Id, "Original implementation notes."),
            });

        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Returns((LlmRequest req, CancellationToken _) => Task.FromResult(
                IsPromptFor(req, _codingAgent)
                    ? new LlmResponse(CodingOutputWithFileChange("Fixed the bug.\nCHANGES: MADE"), "claude-test", 10, 20)
                    : IsPromptFor(req, _testingAgent)
                        ? new LlmResponse("RESULT: PASS", "claude-test", 10, 20)
                        : new LlmResponse("Some output", "claude-test", 10, 20)));

        await _sut.RunPipelineAsync(ticket.Id);

        Assert.Single(ticket.Commits);
        _gitService.Verify(
            g => g.CommitFilesAsync(_project.RepositoryPath, It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), _codingAgent.Name, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunPipelineAsync_WhenCodingOutputsMultipleFileBlocks_CommitsAllOfThemTogether()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        const string codingOutput =
            "I'll update two files.\n" +
            "<file path=\"src/App.tsx\">\nexport const App = () => <div>Hi</div>;\n</file>\n" +
            "<file path=\"src/App.css\">\n.app { color: red; }\n</file>";

        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Returns((LlmRequest req, CancellationToken _) => Task.FromResult(
                IsPromptFor(req, _testingAgent)
                    ? new LlmResponse("RESULT: PASS", "claude-test", 10, 20)
                    : IsPromptFor(req, _codingAgent)
                        ? new LlmResponse(codingOutput, "claude-test", 10, 20)
                        : new LlmResponse("Some output", "claude-test", 10, 20)));

        await _sut.RunPipelineAsync(ticket.Id);

        Assert.Single(ticket.Commits);
        _gitService.Verify(
            g => g.CommitFilesAsync(
                _project.RepositoryPath,
                It.IsAny<string>(),
                It.Is<IReadOnlyDictionary<string, string>>(files =>
                    files.Count == 2 &&
                    files["src/App.tsx"] == "export const App = () => <div>Hi</div>;" &&
                    files["src/App.css"] == ".app { color: red; }"),
                It.IsAny<string>(),
                _codingAgent.Name,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunPipelineAsync_WhenCodingOutputsNoParseableFileBlocks_SkipsTheCommitButStillRecordsAStageExecution()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Returns((LlmRequest req, CancellationToken _) => Task.FromResult(
                IsPromptFor(req, _testingAgent)
                    ? new LlmResponse("RESULT: PASS", "claude-test", 10, 20)
                    : new LlmResponse("Some output", "claude-test", 10, 20)));

        await _sut.RunPipelineAsync(ticket.Id);

        Assert.Empty(ticket.Commits);
        _gitService.Verify(
            g => g.CommitFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _stageExecutionRepository.Verify(
            r => r.AddAsync(It.Is<StageExecution>(e => e.AgentId == _codingAgent.Id && e.Output == "Some output"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunPipelineAsync_HappyPath_RecordsOneStageExecutionPerStageInvocation()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Returns((LlmRequest req, CancellationToken _) => Task.FromResult(
                IsPromptFor(req, _testingAgent)
                    ? new LlmResponse("All checks passed.\nRESULT: PASS", "claude-test", 10, 20)
                    : new LlmResponse("Some output", "claude-test", 10, 20)));

        await _sut.RunPipelineAsync(ticket.Id);

        // Research, Design, Coding, Testing - one invocation each on a clean happy-path run.
        _stageExecutionRepository.Verify(r => r.AddAsync(It.IsAny<StageExecution>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

    [Fact]
    public async Task RunPipelineAsync_WhenAStageAsksAQuestion_BlocksTheTicketWithoutRunningLaterStages()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        var designCalled = false;
        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Returns((LlmRequest req, CancellationToken _) =>
            {
                if (IsPromptFor(req, _designAgent))
                {
                    designCalled = true;
                }

                return Task.FromResult(IsPromptFor(req, _researchAgent)
                    ? new LlmResponse("QUESTION: Which auth provider should this use?", "claude-test", 10, 20)
                    : new LlmResponse("Some output", "claude-test", 10, 20));
            });

        var result = await _sut.RunPipelineAsync(ticket.Id);

        Assert.Equal(TicketStatus.Blocked, ticket.Status);
        Assert.True(result.Blocked);
        Assert.NotNull(result.BlockingQuestionId);
        Assert.False(designCalled);
        _ticketQuestionRepository.Verify(
            r => r.AddAsync(It.Is<TicketQuestion>(q => q.Kind == TicketQuestionKind.Question && q.AgentId == _researchAgent.Id && q.Prompt == "Which auth provider should this use?"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunPipelineAsync_WhenCodingAsksAQuestion_BlocksWithoutCommitting()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Returns((LlmRequest req, CancellationToken _) => Task.FromResult(
                IsPromptFor(req, _codingAgent)
                    ? new LlmResponse("QUESTION: Should I use Redis or in-memory caching?", "claude-test", 10, 20)
                    : new LlmResponse("Some output", "claude-test", 10, 20)));

        var result = await _sut.RunPipelineAsync(ticket.Id);

        Assert.Equal(TicketStatus.Blocked, ticket.Status);
        Assert.True(result.Blocked);
        Assert.Empty(ticket.Commits);
        _gitService.Verify(
            g => g.CommitFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunPipelineAsync_WhenAGitOperationExceptionOccurs_BlocksTheTicketInsteadOfThrowing()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        ticket.AssignAgent(_researchAgent);
        ticket.LinkBranch("existing-branch");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GitOperationException("Push failed: authentication rejected."));

        var result = await _sut.RunPipelineAsync(ticket.Id);

        Assert.Equal(TicketStatus.Blocked, ticket.Status);
        Assert.True(result.Blocked);
        _ticketQuestionRepository.Verify(
            r => r.AddAsync(It.Is<TicketQuestion>(q => q.Kind == TicketQuestionKind.Failure && q.AgentId == _researchAgent.Id), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunPipelineAsync_WhenAnLlmOperationExceptionOccurs_BlocksTheTicketInsteadOfThrowing()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        ticket.AssignAgent(_researchAgent);
        ticket.LinkBranch("existing-branch");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new LlmOperationException("Claude API call failed."));

        var result = await _sut.RunPipelineAsync(ticket.Id);

        Assert.Equal(TicketStatus.Blocked, ticket.Status);
        Assert.True(result.Blocked);
    }

    [Fact]
    public async Task RunPipelineAsync_WhenAnUnexpectedExceptionOccurs_StillPropagates()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        ticket.AssignAgent(_researchAgent);
        ticket.LinkBranch("existing-branch");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);
        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Something genuinely unexpected."));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.RunPipelineAsync(ticket.Id));
        Assert.NotEqual(TicketStatus.Blocked, ticket.Status);
    }

    [Fact]
    public async Task RunPipelineAsync_WhenResumingWithAnAnsweredQuestion_ThreadsItIntoOnlyTheMatchingStageAndMarksItConsumed()
    {
        var ticket = Ticket.Create(_project.Id, "Build feature", "desc");
        _ticketRepository.Setup(r => r.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ticket);

        var answeredQuestion = TicketQuestion.CreateQuestion(ticket.Id, _researchAgent.Id, "Which auth provider?");
        answeredQuestion.Answer("Use Google OAuth.", "Alice");
        _ticketQuestionRepository
            .Setup(r => r.GetMostRecentUnconsumedAnsweredAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(answeredQuestion);

        var researchPrompts = new List<string>();
        var designPrompts = new List<string>();
        _llmConnector
            .Setup(l => l.SendPromptAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Returns((LlmRequest req, CancellationToken _) =>
            {
                if (IsPromptFor(req, _researchAgent))
                {
                    researchPrompts.Add(req.Prompt);
                }

                if (IsPromptFor(req, _designAgent))
                {
                    designPrompts.Add(req.Prompt);
                }

                return Task.FromResult(IsPromptFor(req, _testingAgent)
                    ? new LlmResponse("RESULT: PASS", "claude-test", 10, 20)
                    : new LlmResponse("Some output", "claude-test", 10, 20));
            });

        await _sut.RunPipelineAsync(ticket.Id);

        Assert.Contains("Which auth provider?", researchPrompts[0]);
        Assert.Contains("Use Google OAuth.", researchPrompts[0]);
        Assert.DoesNotContain("Which auth provider?", designPrompts[0]);
        Assert.True(answeredQuestion.Consumed);
    }
}
