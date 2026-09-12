using FluentValidation;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Extensions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Pipelines.Dtos;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;

namespace TeamPilot.Application.Pipelines;

public sealed class PipelineService(
    IPipelineRunRepository pipelineRunRepository,
    IProjectAccessGuard projectAccessGuard,
    IAuditLogger auditLogger,
    IUnitOfWork unitOfWork,
    IValidator<CompletePipelineRunRequest> completeValidator) : IPipelineService
{
    public async Task<PipelineRunDto> TriggerAsync(Guid projectId, Guid? ticketId, string triggerReason, CancellationToken cancellationToken = default)
    {
        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);

        var pipelineRun = PipelineRun.Create(projectId, ticketId, triggerReason);
        await pipelineRunRepository.AddAsync(pipelineRun, cancellationToken);

        await auditLogger.LogActionAsync(AuditEventType.PipelineRunTriggered, $"Pipeline run triggered: {triggerReason}", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(pipelineRun);
    }

    public async Task<PipelineRunDto> StartAsync(Guid pipelineRunId, CancellationToken cancellationToken = default)
    {
        var pipelineRun = await pipelineRunRepository.GetByIdAsync(pipelineRunId, cancellationToken)
            ?? throw new NotFoundException(nameof(PipelineRun), pipelineRunId);

        await projectAccessGuard.EnsureAccessAsync(pipelineRun.ProjectId, cancellationToken);

        pipelineRun.Start();

        await auditLogger.LogActionAsync(AuditEventType.PipelineRunStarted, $"Pipeline run {pipelineRun.Id} started.", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(pipelineRun);
    }

    public async Task<PipelineRunDto> CompleteAsync(Guid pipelineRunId, CompletePipelineRunRequest request, CancellationToken cancellationToken = default)
    {
        await completeValidator.EnsureValidAsync(request, cancellationToken);

        var pipelineRun = await pipelineRunRepository.GetByIdAsync(pipelineRunId, cancellationToken)
            ?? throw new NotFoundException(nameof(PipelineRun), pipelineRunId);

        await projectAccessGuard.EnsureAccessAsync(pipelineRun.ProjectId, cancellationToken);

        pipelineRun.Complete(request.Succeeded, request.LogOutput);

        await auditLogger.LogActionAsync(AuditEventType.PipelineRunCompleted, $"Pipeline run {pipelineRun.Id} completed ({(request.Succeeded ? "Succeeded" : "Failed")}).", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(pipelineRun);
    }

    public async Task<IReadOnlyList<PipelineRunDto>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await projectAccessGuard.EnsureAccessAsync(projectId, cancellationToken);

        var pipelineRuns = await pipelineRunRepository.ListByProjectAsync(projectId, cancellationToken);
        return pipelineRuns.Select(ToDto).ToList();
    }

    private static PipelineRunDto ToDto(PipelineRun pipelineRun) => new(
        pipelineRun.Id,
        pipelineRun.ProjectId,
        pipelineRun.TicketId,
        pipelineRun.Status,
        pipelineRun.TriggerReason,
        pipelineRun.LogOutput,
        pipelineRun.StartedAtUtc,
        pipelineRun.CompletedAtUtc,
        pipelineRun.CreatedAtUtc);
}
