using FluentValidation;
using TeamPilot.Application.Agents;
using TeamPilot.Application.Agents.Dtos;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Extensions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Tickets;
using TeamPilot.Application.Workflow.Dtos;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Workflow;

public sealed class WorkflowService(
    IWorkflowStageRepository workflowStageRepository,
    IAgentRepository agentRepository,
    IAgentService agentService,
    ITicketRepository ticketRepository,
    IProjectAccessGuard projectAccessGuard,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork,
    IValidator<CreateCustomAgentRequest> createCustomAgentValidator,
    IValidator<ReorderWorkflowRequest> reorderValidator,
    IValidator<SetWorkflowLoopBackRequest> setLoopBackValidator) : IWorkflowService
{
    /// <summary>Reproduces today's hardcoded Testing-fails-retry-Coding behavior as data instead
    /// of C# - see <c>TeamPilot.Application.Orchestration.OrchestrationService</c>.</summary>
    private const int DefaultMaxLoopIterations = 3;

    public async Task EnsureDefaultWorkflowAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var existingStages = await workflowStageRepository.ListOrderedAsync(projectId, cancellationToken);
        if (existingStages.Count > 0)
        {
            return;
        }

        await agentService.EnsureDefaultAgentsAsync(projectId, cancellationToken);

        var research = await GetPipelineAgentAsync(projectId, AgentRole.Research, cancellationToken);
        var design = await GetPipelineAgentAsync(projectId, AgentRole.Design, cancellationToken);
        var coding = await GetPipelineAgentAsync(projectId, AgentRole.Coding, cancellationToken);
        var testing = await GetPipelineAgentAsync(projectId, AgentRole.Testing, cancellationToken);

        var researchStage = WorkflowStage.Create(projectId, research.Id, 0);
        var designStage = WorkflowStage.Create(projectId, design.Id, 1);
        var codingStage = WorkflowStage.Create(projectId, coding.Id, 2);
        var testingStage = WorkflowStage.Create(projectId, testing.Id, 3);
        testingStage.SetLoopBack(codingStage.Id, DefaultMaxLoopIterations);

        await workflowStageRepository.AddAsync(researchStage, cancellationToken);
        await workflowStageRepository.AddAsync(designStage, cancellationToken);
        await workflowStageRepository.AddAsync(codingStage, cancellationToken);
        await workflowStageRepository.AddAsync(testingStage, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowStageDto>> ListAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);

        var stages = await workflowStageRepository.ListOrderedAsync(projectId, cancellationToken);
        var agentsById = await GetAgentsByIdAsync(projectId, cancellationToken);

        return stages.Select(s => ToDto(s, agentsById[s.AgentId])).ToList();
    }

    public async Task<IReadOnlyList<AgentDto>> ListUnscheduledAgentsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);

        var agents = await agentRepository.ListAsync(projectId, cancellationToken);
        var stages = await workflowStageRepository.ListOrderedAsync(projectId, cancellationToken);
        var scheduledAgentIds = stages.Select(s => s.AgentId).ToHashSet();

        return agents
            .Where(a => a.Role != AgentRole.LiveAgent && !scheduledAgentIds.Contains(a.Id))
            .Select(ToAgentDto)
            .ToList();
    }

    public async Task<AgentDto> CreateCustomAgentAsync(Guid projectId, CreateCustomAgentRequest request, CancellationToken cancellationToken = default)
    {
        await createCustomAgentValidator.EnsureValidAsync(request, cancellationToken);
        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);

        // Not gated by the InProgress lock: a blank, unscheduled agent can't affect any ticket's
        // pipeline until it's explicitly placed into the sequence via AddExistingAgentAsync.
        var agent = Agent.Create(projectId, request.Name, AgentRole.Custom);
        await agentRepository.AddAsync(agent, cancellationToken);

        await auditLogger.LogActionAsync(AuditEventType.AgentCreated, $"Custom agent '{agent.Name}' created for project '{projectId}'.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToAgentDto(agent);
    }

    public async Task DeleteCustomAgentAsync(Guid projectId, Guid agentId, CancellationToken cancellationToken = default)
    {
        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);

        var agent = await agentRepository.GetByIdAsync(agentId, cancellationToken)
            ?? throw new NotFoundException(nameof(Agent), agentId);

        if (agent.ProjectId != projectId)
        {
            throw new NotFoundException(nameof(Agent), agentId);
        }

        if (agent.Role != AgentRole.Custom)
        {
            throw new InvalidWorkflowOperationException($"'{agent.Name}' is a {agent.Role} agent and can't be deleted - only a custom agent can be.");
        }

        var stages = await workflowStageRepository.ListOrderedAsync(projectId, cancellationToken);
        if (stages.Any(s => s.AgentId == agent.Id))
        {
            throw new InvalidWorkflowOperationException($"Remove '{agent.Name}' from the pipeline before deleting it.");
        }

        if (await agentRepository.HasAssignmentHistoryAsync(agent.Id, cancellationToken))
        {
            throw new InvalidWorkflowOperationException($"'{agent.Name}' has already worked on a ticket and can't be deleted.");
        }

        // Not gated by the InProgress lock: an unscheduled agent can't affect any running ticket.
        agentRepository.Remove(agent);

        await auditLogger.LogActionAsync(AuditEventType.AgentDeleted, $"Custom agent '{agent.Name}' deleted from project '{projectId}'.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<WorkflowStageDto> AddExistingAgentAsync(Guid projectId, AddWorkflowStageRequest request, CancellationToken cancellationToken = default)
    {
        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);
        await EnsureNotLockedAsync(projectId, cancellationToken);

        var agent = await agentRepository.GetByIdAsync(request.AgentId, cancellationToken)
            ?? throw new NotFoundException(nameof(Agent), request.AgentId);

        if (agent.ProjectId != projectId)
        {
            throw new NotFoundException(nameof(Agent), request.AgentId);
        }

        if (!agent.HasCompleteInstructions)
        {
            throw new AgentInstructionsIncompleteException(agent.Name);
        }

        var stages = await workflowStageRepository.ListOrderedAsync(projectId, cancellationToken);
        if (stages.Any(s => s.AgentId == agent.Id))
        {
            throw new InvalidWorkflowOperationException($"Agent '{agent.Name}' is already part of this project's pipeline.");
        }

        var stage = WorkflowStage.Create(projectId, agent.Id, stages.Count);
        await workflowStageRepository.AddAsync(stage, cancellationToken);

        await auditLogger.LogActionAsync(AuditEventType.WorkflowStageAdded, $"Agent '{agent.Name}' added to the pipeline at position {stage.Order + 1}.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(stage, agent);
    }

    public async Task RemoveStageAsync(Guid projectId, Guid stageId, CancellationToken cancellationToken = default)
    {
        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);
        await EnsureNotLockedAsync(projectId, cancellationToken);

        var stages = await workflowStageRepository.ListOrderedAsync(projectId, cancellationToken);
        var stage = stages.FirstOrDefault(s => s.Id == stageId)
            ?? throw new NotFoundException(nameof(WorkflowStage), stageId);

        var blockingStage = stages.FirstOrDefault(s => s.LoopBackToStageId == stageId);
        if (blockingStage is not null)
        {
            throw new InvalidWorkflowOperationException(
                "Cannot remove a stage that another stage loops back to - clear that loop-back first.");
        }

        var remaining = stages.Where(s => s.Id != stageId).OrderBy(s => s.Order).ToList();
        for (var i = 0; i < remaining.Count; i++)
        {
            remaining[i].MoveTo(i);
        }

        workflowStageRepository.Remove(stage);

        await auditLogger.LogActionAsync(AuditEventType.WorkflowStageRemoved, $"Stage at position {stage.Order + 1} removed from the pipeline.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowStageDto>> ReorderAsync(Guid projectId, ReorderWorkflowRequest request, CancellationToken cancellationToken = default)
    {
        await reorderValidator.EnsureValidAsync(request, cancellationToken);
        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);
        await EnsureNotLockedAsync(projectId, cancellationToken);

        var stages = await workflowStageRepository.ListOrderedAsync(projectId, cancellationToken);
        var currentIds = stages.Select(s => s.Id).ToHashSet();
        var requestedIds = request.StageIds.ToHashSet();

        if (currentIds.Count != request.StageIds.Count || !currentIds.SetEquals(requestedIds))
        {
            throw new InvalidWorkflowOperationException(
                "The reorder request must include exactly the project's current pipeline stages, each once.");
        }

        var newOrderByStageId = request.StageIds
            .Select((id, index) => (id, index))
            .ToDictionary(x => x.id, x => x.index);

        foreach (var stage in stages)
        {
            if (stage.LoopBackToStageId is Guid targetId && newOrderByStageId[targetId] >= newOrderByStageId[stage.Id])
            {
                throw new InvalidWorkflowOperationException(
                    "This reorder would move a stage's loop-back target to no longer be earlier in the sequence - clear or update that loop-back first.");
            }
        }

        foreach (var stage in stages)
        {
            stage.MoveTo(newOrderByStageId[stage.Id]);
        }

        await auditLogger.LogActionAsync(AuditEventType.WorkflowReordered, $"Pipeline reordered for project '{projectId}'.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var agentsById = await GetAgentsByIdAsync(projectId, cancellationToken);
        return stages.OrderBy(s => s.Order).Select(s => ToDto(s, agentsById[s.AgentId])).ToList();
    }

    public async Task<WorkflowStageDto> SetLoopBackAsync(Guid projectId, Guid stageId, SetWorkflowLoopBackRequest request, CancellationToken cancellationToken = default)
    {
        await setLoopBackValidator.EnsureValidAsync(request, cancellationToken);
        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);
        await EnsureNotLockedAsync(projectId, cancellationToken);

        var stages = await workflowStageRepository.ListOrderedAsync(projectId, cancellationToken);
        var stage = stages.FirstOrDefault(s => s.Id == stageId)
            ?? throw new NotFoundException(nameof(WorkflowStage), stageId);
        var target = stages.FirstOrDefault(s => s.Id == request.TargetStageId)
            ?? throw new InvalidWorkflowOperationException("Loop-back target must be another stage in this project's pipeline.");

        if (target.Order >= stage.Order)
        {
            throw new InvalidWorkflowOperationException("Loop-back target must be an earlier stage in the sequence.");
        }

        stage.SetLoopBack(target.Id, request.MaxLoopIterations);

        await auditLogger.LogActionAsync(AuditEventType.WorkflowLoopBackSet, $"Loop-back set for stage at position {stage.Order + 1}, targeting position {target.Order + 1}.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var agentsById = await GetAgentsByIdAsync(projectId, cancellationToken);
        return ToDto(stage, agentsById[stage.AgentId]);
    }

    public async Task<WorkflowStageDto> ClearLoopBackAsync(Guid projectId, Guid stageId, CancellationToken cancellationToken = default)
    {
        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);
        await EnsureNotLockedAsync(projectId, cancellationToken);

        var tracked = await workflowStageRepository.GetByIdAsync(stageId, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkflowStage), stageId);

        if (tracked.ProjectId != projectId)
        {
            throw new NotFoundException(nameof(WorkflowStage), stageId);
        }

        tracked.ClearLoopBack();

        await auditLogger.LogActionAsync(AuditEventType.WorkflowLoopBackCleared, $"Loop-back cleared for a pipeline stage in project '{projectId}'.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var agent = await agentRepository.GetByIdAsync(tracked.AgentId, cancellationToken)
            ?? throw new NotFoundException(nameof(Agent), tracked.AgentId);
        return ToDto(tracked, agent);
    }

    private async Task EnsureNotLockedAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var inProgressCount = await ticketRepository.CountByStatusAsync(projectId, TicketStatus.InProgress, cancellationToken);
        var blockedCount = await ticketRepository.CountByStatusAsync(projectId, TicketStatus.Blocked, cancellationToken);
        var lockingCount = inProgressCount + blockedCount;
        if (lockingCount > 0)
        {
            throw new WorkflowLockedException(lockingCount);
        }
    }

    private async Task<Agent> GetPipelineAgentAsync(Guid projectId, AgentRole role, CancellationToken cancellationToken) =>
        await agentRepository.GetByProjectAndRoleAsync(projectId, role, cancellationToken)
            ?? throw new InvalidOperationException($"Project '{projectId}' has no active {role} agent.");

    private async Task<Dictionary<Guid, Agent>> GetAgentsByIdAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var agents = await agentRepository.ListAsync(projectId, cancellationToken);
        return agents.ToDictionary(a => a.Id);
    }

    private static WorkflowStageDto ToDto(WorkflowStage stage, Agent agent) => new(
        stage.Id,
        stage.ProjectId,
        stage.Order,
        ToAgentDto(agent),
        stage.LoopBackToStageId,
        stage.MaxLoopIterations);

    private static AgentDto ToAgentDto(Agent agent) => new(
        agent.Id,
        agent.ProjectId,
        agent.Name,
        agent.Role,
        agent.Status,
        agent.ConfigurationJson,
        agent.CreatedAtUtc,
        agent.UpdatedAtUtc);
}
