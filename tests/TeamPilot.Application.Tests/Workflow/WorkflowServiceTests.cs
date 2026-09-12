using Moq;
using TeamPilot.Application.Agents;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Tickets;
using TeamPilot.Application.Workflow;
using TeamPilot.Application.Workflow.Dtos;
using TeamPilot.Application.Workflow.Validators;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using Xunit;

namespace TeamPilot.Application.Tests.Workflow;

public class WorkflowServiceTests
{
    private readonly Mock<IWorkflowStageRepository> _workflowStageRepository = new();
    private readonly Mock<IAgentRepository> _agentRepository = new();
    private readonly Mock<IAgentService> _agentService = new();
    private readonly Mock<ITicketRepository> _ticketRepository = new();
    private readonly Mock<IProjectAccessGuard> _projectAccessGuard = new();
    private readonly Mock<IAuditLogger> _auditLogger = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly WorkflowService _sut;
    private readonly Guid _projectId = Guid.NewGuid();

    public WorkflowServiceTests()
    {
        _ticketRepository
            .Setup(r => r.CountByStatusAsync(_projectId, TicketStatus.InProgress, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _sut = new WorkflowService(
            _workflowStageRepository.Object,
            _agentRepository.Object,
            _agentService.Object,
            _ticketRepository.Object,
            _projectAccessGuard.Object,
            _auditLogger.Object,
            _unitOfWork.Object,
            new CreateCustomAgentRequestValidator(),
            new ReorderWorkflowRequestValidator(),
            new SetWorkflowLoopBackRequestValidator());
    }

    private Agent CompleteCustomAgent(string name = "Reviewer")
    {
        var agent = Agent.Create(_projectId, name, AgentRole.Custom);
        agent.AddInstructionVersion(InstructionType.Constitution, "c", "admin");
        agent.AddInstructionVersion(InstructionType.Guideline, "g", "admin");
        agent.AddInstructionVersion(InstructionType.Requirement, "r", "admin");
        return agent;
    }

    [Fact]
    public async Task EnsureDefaultWorkflowAsync_WhenProjectAlreadyHasStages_DoesNothing()
    {
        var existing = WorkflowStage.Create(_projectId, Guid.NewGuid(), 0);
        _workflowStageRepository.Setup(r => r.ListOrderedAsync(_projectId, It.IsAny<CancellationToken>())).ReturnsAsync([existing]);

        await _sut.EnsureDefaultWorkflowAsync(_projectId);

        _agentService.Verify(s => s.EnsureDefaultAgentsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _workflowStageRepository.Verify(r => r.AddAsync(It.IsAny<WorkflowStage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnsureDefaultWorkflowAsync_WhenProjectHasNoStages_CreatesFourStagesWithTestingLoopingBackToCoding()
    {
        _workflowStageRepository.Setup(r => r.ListOrderedAsync(_projectId, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var research = Agent.Create(_projectId, "Research Agent", AgentRole.Research);
        var design = Agent.Create(_projectId, "Design Agent", AgentRole.Design);
        var coding = Agent.Create(_projectId, "Coding Agent", AgentRole.Coding);
        var testing = Agent.Create(_projectId, "Testing Agent", AgentRole.Testing);
        _agentRepository.Setup(r => r.GetByProjectAndRoleAsync(_projectId, AgentRole.Research, It.IsAny<CancellationToken>())).ReturnsAsync(research);
        _agentRepository.Setup(r => r.GetByProjectAndRoleAsync(_projectId, AgentRole.Design, It.IsAny<CancellationToken>())).ReturnsAsync(design);
        _agentRepository.Setup(r => r.GetByProjectAndRoleAsync(_projectId, AgentRole.Coding, It.IsAny<CancellationToken>())).ReturnsAsync(coding);
        _agentRepository.Setup(r => r.GetByProjectAndRoleAsync(_projectId, AgentRole.Testing, It.IsAny<CancellationToken>())).ReturnsAsync(testing);

        WorkflowStage? codingStage = null;
        WorkflowStage? testingStage = null;
        _workflowStageRepository
            .Setup(r => r.AddAsync(It.IsAny<WorkflowStage>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowStage, CancellationToken>((s, _) =>
            {
                if (s.AgentId == coding.Id)
                {
                    codingStage = s;
                }

                if (s.AgentId == testing.Id)
                {
                    testingStage = s;
                }
            })
            .Returns(Task.CompletedTask);

        await _sut.EnsureDefaultWorkflowAsync(_projectId);

        _agentService.Verify(s => s.EnsureDefaultAgentsAsync(_projectId, It.IsAny<CancellationToken>()), Times.Once);
        _workflowStageRepository.Verify(r => r.AddAsync(It.IsAny<WorkflowStage>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
        Assert.NotNull(codingStage);
        Assert.NotNull(testingStage);
        Assert.Equal(codingStage!.Id, testingStage!.LoopBackToStageId);
        Assert.Equal(3, testingStage.MaxLoopIterations);
    }

    [Fact]
    public async Task CreateCustomAgentAsync_CreatesAnUnscheduledBlankAgent()
    {
        var request = new CreateCustomAgentRequest("Reviewer");

        var result = await _sut.CreateCustomAgentAsync(_projectId, request);

        Assert.Equal(AgentRole.Custom, result.Role);
        _agentRepository.Verify(r => r.AddAsync(It.Is<Agent>(a => a.Name == "Reviewer" && a.Role == AgentRole.Custom), It.IsAny<CancellationToken>()), Times.Once);
        _auditLogger.Verify(a => a.LogActionAsync(AuditEventType.AgentCreated, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateCustomAgentAsync_WithBlankName_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateCustomAgentAsync(_projectId, new CreateCustomAgentRequest(string.Empty)));
    }

    [Fact]
    public async Task CreateCustomAgentAsync_EvenWithAnInProgressTicket_Succeeds()
    {
        _ticketRepository
            .Setup(r => r.CountByStatusAsync(_projectId, TicketStatus.InProgress, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await _sut.CreateCustomAgentAsync(_projectId, new CreateCustomAgentRequest("Reviewer"));

        Assert.Equal("Reviewer", result.Name);
    }

    [Fact]
    public async Task DeleteCustomAgentAsync_ForNonCustomAgent_ThrowsInvalidWorkflowOperationException()
    {
        var agent = Agent.Create(_projectId, "Testing Agent", AgentRole.Testing);
        _agentRepository.Setup(r => r.GetByIdAsync(agent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agent);

        await Assert.ThrowsAsync<InvalidWorkflowOperationException>(() => _sut.DeleteCustomAgentAsync(_projectId, agent.Id));

        _agentRepository.Verify(r => r.Remove(It.IsAny<Agent>()), Times.Never);
    }

    [Fact]
    public async Task DeleteCustomAgentAsync_ForAScheduledAgent_ThrowsInvalidWorkflowOperationException()
    {
        var agent = CompleteCustomAgent();
        _agentRepository.Setup(r => r.GetByIdAsync(agent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agent);
        _workflowStageRepository
            .Setup(r => r.ListOrderedAsync(_projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([WorkflowStage.Create(_projectId, agent.Id, 0)]);

        await Assert.ThrowsAsync<InvalidWorkflowOperationException>(() => _sut.DeleteCustomAgentAsync(_projectId, agent.Id));

        _agentRepository.Verify(r => r.Remove(It.IsAny<Agent>()), Times.Never);
    }

    [Fact]
    public async Task DeleteCustomAgentAsync_ForAgentWithAssignmentHistory_ThrowsInvalidWorkflowOperationException()
    {
        var agent = CompleteCustomAgent();
        _agentRepository.Setup(r => r.GetByIdAsync(agent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agent);
        _workflowStageRepository.Setup(r => r.ListOrderedAsync(_projectId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _agentRepository.Setup(r => r.HasAssignmentHistoryAsync(agent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Assert.ThrowsAsync<InvalidWorkflowOperationException>(() => _sut.DeleteCustomAgentAsync(_projectId, agent.Id));

        _agentRepository.Verify(r => r.Remove(It.IsAny<Agent>()), Times.Never);
    }

    [Fact]
    public async Task DeleteCustomAgentAsync_ForUnscheduledAgentWithNoHistory_RemovesItAndAudits()
    {
        var agent = CompleteCustomAgent();
        _agentRepository.Setup(r => r.GetByIdAsync(agent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agent);
        _workflowStageRepository.Setup(r => r.ListOrderedAsync(_projectId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _agentRepository.Setup(r => r.HasAssignmentHistoryAsync(agent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await _sut.DeleteCustomAgentAsync(_projectId, agent.Id);

        _agentRepository.Verify(r => r.Remove(agent), Times.Once);
        _auditLogger.Verify(a => a.LogActionAsync(AuditEventType.AgentDeleted, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteCustomAgentAsync_EvenWithAnInProgressTicket_Succeeds()
    {
        var agent = CompleteCustomAgent();
        _agentRepository.Setup(r => r.GetByIdAsync(agent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agent);
        _workflowStageRepository.Setup(r => r.ListOrderedAsync(_projectId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _agentRepository.Setup(r => r.HasAssignmentHistoryAsync(agent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _ticketRepository
            .Setup(r => r.CountByStatusAsync(_projectId, TicketStatus.InProgress, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await _sut.DeleteCustomAgentAsync(_projectId, agent.Id);

        _agentRepository.Verify(r => r.Remove(agent), Times.Once);
    }

    [Fact]
    public async Task AddExistingAgentAsync_WithIncompleteInstructions_ThrowsAgentInstructionsIncompleteException()
    {
        var agent = Agent.Create(_projectId, "Reviewer", AgentRole.Custom);
        _agentRepository.Setup(r => r.GetByIdAsync(agent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agent);
        _workflowStageRepository.Setup(r => r.ListOrderedAsync(_projectId, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await Assert.ThrowsAsync<AgentInstructionsIncompleteException>(
            () => _sut.AddExistingAgentAsync(_projectId, new AddWorkflowStageRequest(agent.Id)));
    }

    [Fact]
    public async Task AddExistingAgentAsync_WithCompleteInstructions_AppendsStageAtTheEnd()
    {
        var agent = CompleteCustomAgent();
        _agentRepository.Setup(r => r.GetByIdAsync(agent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agent);
        var existingStage = WorkflowStage.Create(_projectId, Guid.NewGuid(), 0);
        _workflowStageRepository.Setup(r => r.ListOrderedAsync(_projectId, It.IsAny<CancellationToken>())).ReturnsAsync([existingStage]);

        var result = await _sut.AddExistingAgentAsync(_projectId, new AddWorkflowStageRequest(agent.Id));

        Assert.Equal(1, result.Order);
        _auditLogger.Verify(a => a.LogActionAsync(AuditEventType.WorkflowStageAdded, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddExistingAgentAsync_WhileProjectHasAnInProgressTicket_ThrowsWorkflowLockedException()
    {
        _ticketRepository
            .Setup(r => r.CountByStatusAsync(_projectId, TicketStatus.InProgress, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        var agent = CompleteCustomAgent();

        await Assert.ThrowsAsync<WorkflowLockedException>(
            () => _sut.AddExistingAgentAsync(_projectId, new AddWorkflowStageRequest(agent.Id)));
    }

    [Fact]
    public async Task RemoveStageAsync_WhenAnotherStageLoopsBackToIt_ThrowsInvalidWorkflowOperationException()
    {
        var codingStage = WorkflowStage.Create(_projectId, Guid.NewGuid(), 0);
        var testingStage = WorkflowStage.Create(_projectId, Guid.NewGuid(), 1);
        testingStage.SetLoopBack(codingStage.Id, 3);
        _workflowStageRepository.Setup(r => r.ListOrderedAsync(_projectId, It.IsAny<CancellationToken>())).ReturnsAsync([codingStage, testingStage]);

        await Assert.ThrowsAsync<InvalidWorkflowOperationException>(() => _sut.RemoveStageAsync(_projectId, codingStage.Id));
    }

    [Fact]
    public async Task RemoveStageAsync_WhileProjectHasAnInProgressTicket_ThrowsWorkflowLockedException()
    {
        _ticketRepository
            .Setup(r => r.CountByStatusAsync(_projectId, TicketStatus.InProgress, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await Assert.ThrowsAsync<WorkflowLockedException>(() => _sut.RemoveStageAsync(_projectId, Guid.NewGuid()));
    }

    [Fact]
    public async Task RemoveStageAsync_WithNoBlockingLoopBack_RemovesAndRenumbersRemainingStages()
    {
        var stageA = WorkflowStage.Create(_projectId, Guid.NewGuid(), 0);
        var stageB = WorkflowStage.Create(_projectId, Guid.NewGuid(), 1);
        var stageC = WorkflowStage.Create(_projectId, Guid.NewGuid(), 2);
        _workflowStageRepository.Setup(r => r.ListOrderedAsync(_projectId, It.IsAny<CancellationToken>())).ReturnsAsync([stageA, stageB, stageC]);

        await _sut.RemoveStageAsync(_projectId, stageA.Id);

        Assert.Equal(0, stageB.Order);
        Assert.Equal(1, stageC.Order);
        _workflowStageRepository.Verify(r => r.Remove(stageA), Times.Once);
    }

    [Fact]
    public async Task ReorderAsync_WithMismatchedStageIds_ThrowsInvalidWorkflowOperationException()
    {
        var stageA = WorkflowStage.Create(_projectId, Guid.NewGuid(), 0);
        var stageB = WorkflowStage.Create(_projectId, Guid.NewGuid(), 1);
        _workflowStageRepository.Setup(r => r.ListOrderedAsync(_projectId, It.IsAny<CancellationToken>())).ReturnsAsync([stageA, stageB]);

        var request = new ReorderWorkflowRequest([stageA.Id, Guid.NewGuid()]);

        await Assert.ThrowsAsync<InvalidWorkflowOperationException>(() => _sut.ReorderAsync(_projectId, request));
    }

    [Fact]
    public async Task ReorderAsync_WhenItWouldMoveALoopBackTargetLater_ThrowsInvalidWorkflowOperationException()
    {
        var codingStage = WorkflowStage.Create(_projectId, Guid.NewGuid(), 0);
        var testingStage = WorkflowStage.Create(_projectId, Guid.NewGuid(), 1);
        testingStage.SetLoopBack(codingStage.Id, 3);
        _workflowStageRepository.Setup(r => r.ListOrderedAsync(_projectId, It.IsAny<CancellationToken>())).ReturnsAsync([codingStage, testingStage]);

        // Puts Testing before Coding - Coding would no longer be earlier than its own looper.
        var request = new ReorderWorkflowRequest([testingStage.Id, codingStage.Id]);

        await Assert.ThrowsAsync<InvalidWorkflowOperationException>(() => _sut.ReorderAsync(_projectId, request));
    }

    [Fact]
    public async Task ReorderAsync_WhileProjectHasAnInProgressTicket_ThrowsWorkflowLockedException()
    {
        _ticketRepository
            .Setup(r => r.CountByStatusAsync(_projectId, TicketStatus.InProgress, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await Assert.ThrowsAsync<WorkflowLockedException>(() => _sut.ReorderAsync(_projectId, new ReorderWorkflowRequest([Guid.NewGuid()])));
    }

    [Fact]
    public async Task ReorderAsync_WithValidPermutation_RenumbersStagesAndSaves()
    {
        var agentA = Agent.Create(_projectId, "A", AgentRole.Custom);
        var agentB = Agent.Create(_projectId, "B", AgentRole.Custom);
        var stageA = WorkflowStage.Create(_projectId, agentA.Id, 0);
        var stageB = WorkflowStage.Create(_projectId, agentB.Id, 1);
        _workflowStageRepository.Setup(r => r.ListOrderedAsync(_projectId, It.IsAny<CancellationToken>())).ReturnsAsync([stageA, stageB]);
        _agentRepository.Setup(r => r.ListAsync(_projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([agentA, agentB]);

        var result = await _sut.ReorderAsync(_projectId, new ReorderWorkflowRequest([stageB.Id, stageA.Id]));

        Assert.Equal(0, stageB.Order);
        Assert.Equal(1, stageA.Order);
        _auditLogger.Verify(a => a.LogActionAsync(AuditEventType.WorkflowReordered, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task SetLoopBackAsync_TargetingALaterStage_ThrowsInvalidWorkflowOperationException()
    {
        var stageA = WorkflowStage.Create(_projectId, Guid.NewGuid(), 0);
        var stageB = WorkflowStage.Create(_projectId, Guid.NewGuid(), 1);
        _workflowStageRepository.Setup(r => r.ListOrderedAsync(_projectId, It.IsAny<CancellationToken>())).ReturnsAsync([stageA, stageB]);

        var request = new SetWorkflowLoopBackRequest(stageB.Id, 3);

        await Assert.ThrowsAsync<InvalidWorkflowOperationException>(() => _sut.SetLoopBackAsync(_projectId, stageA.Id, request));
    }

    [Fact]
    public async Task SetLoopBackAsync_TargetingAnEarlierStage_SetsLoopBack()
    {
        var agentA = Agent.Create(_projectId, "A", AgentRole.Custom);
        var agentB = Agent.Create(_projectId, "B", AgentRole.Custom);
        var stageA = WorkflowStage.Create(_projectId, agentA.Id, 0);
        var stageB = WorkflowStage.Create(_projectId, agentB.Id, 1);
        _workflowStageRepository.Setup(r => r.ListOrderedAsync(_projectId, It.IsAny<CancellationToken>())).ReturnsAsync([stageA, stageB]);
        _agentRepository.Setup(r => r.ListAsync(_projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([agentA, agentB]);

        var result = await _sut.SetLoopBackAsync(_projectId, stageB.Id, new SetWorkflowLoopBackRequest(stageA.Id, 5));

        Assert.Equal(stageA.Id, result.LoopBackToStageId);
        Assert.Equal(5, result.MaxLoopIterations);
        _auditLogger.Verify(a => a.LogActionAsync(AuditEventType.WorkflowLoopBackSet, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetLoopBackAsync_WhileProjectHasAnInProgressTicket_ThrowsWorkflowLockedException()
    {
        _ticketRepository
            .Setup(r => r.CountByStatusAsync(_projectId, TicketStatus.InProgress, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await Assert.ThrowsAsync<WorkflowLockedException>(
            () => _sut.SetLoopBackAsync(_projectId, Guid.NewGuid(), new SetWorkflowLoopBackRequest(Guid.NewGuid(), 3)));
    }

    [Fact]
    public async Task ClearLoopBackAsync_RemovesTheLoopBack()
    {
        var stageA = WorkflowStage.Create(_projectId, Guid.NewGuid(), 0);
        var stageB = WorkflowStage.Create(_projectId, Guid.NewGuid(), 1);
        stageB.SetLoopBack(stageA.Id, 3);
        _workflowStageRepository.Setup(r => r.GetByIdAsync(stageB.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stageB);
        _agentRepository.Setup(r => r.GetByIdAsync(stageB.AgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Agent.Create(_projectId, "B", AgentRole.Custom));

        var result = await _sut.ClearLoopBackAsync(_projectId, stageB.Id);

        Assert.Null(result.LoopBackToStageId);
        Assert.Null(result.MaxLoopIterations);
        _auditLogger.Verify(a => a.LogActionAsync(AuditEventType.WorkflowLoopBackCleared, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClearLoopBackAsync_WhileProjectHasAnInProgressTicket_ThrowsWorkflowLockedException()
    {
        _ticketRepository
            .Setup(r => r.CountByStatusAsync(_projectId, TicketStatus.InProgress, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await Assert.ThrowsAsync<WorkflowLockedException>(() => _sut.ClearLoopBackAsync(_projectId, Guid.NewGuid()));
    }

    [Fact]
    public async Task ListUnscheduledAgentsAsync_ExcludesScheduledAgentsAndTheLiveAgent()
    {
        var scheduled = Agent.Create(_projectId, "Coding Agent", AgentRole.Coding);
        var unscheduled = Agent.Create(_projectId, "Reviewer", AgentRole.Custom);
        var liveAgent = Agent.Create(_projectId, "Live Agent", AgentRole.LiveAgent);
        _agentRepository.Setup(r => r.ListAsync(_projectId, It.IsAny<CancellationToken>())).ReturnsAsync([scheduled, unscheduled, liveAgent]);
        _workflowStageRepository
            .Setup(r => r.ListOrderedAsync(_projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([WorkflowStage.Create(_projectId, scheduled.Id, 0)]);

        var result = await _sut.ListUnscheduledAgentsAsync(_projectId);

        Assert.Single(result);
        Assert.Equal(unscheduled.Id, result[0].Id);
    }
}
