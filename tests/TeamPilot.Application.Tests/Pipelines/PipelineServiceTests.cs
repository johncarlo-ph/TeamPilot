using Moq;
using TeamPilot.Application.Auth;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.Pipelines;
using TeamPilot.Application.Pipelines.Dtos;
using TeamPilot.Application.Pipelines.Validators;
using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using Xunit;

namespace TeamPilot.Application.Tests.Pipelines;

public class PipelineServiceTests
{
    private readonly Mock<IPipelineRunRepository> _pipelineRunRepository = new();
    private readonly Mock<IProjectAccessGuard> _projectAccessGuard = new();
    private readonly Mock<IAuditLogger> _auditLogger = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly PipelineService _sut;

    public PipelineServiceTests()
    {
        _sut = new PipelineService(
            _pipelineRunRepository.Object,
            _projectAccessGuard.Object,
            _auditLogger.Object,
            _unitOfWork.Object,
            new CompletePipelineRunRequestValidator());
    }

    [Fact]
    public async Task TriggerAsync_WithValidArguments_QueuesPipelineRun()
    {
        var projectId = Guid.NewGuid();

        var result = await _sut.TriggerAsync(projectId, ticketId: null, "Manual trigger");

        Assert.Equal(PipelineRunStatus.Queued, result.Status);
        _pipelineRunRepository.Verify(r => r.AddAsync(It.IsAny<PipelineRun>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_WhenPipelineRunIsQueued_SetsStatusToRunning()
    {
        var pipelineRun = PipelineRun.Create(Guid.NewGuid(), null, "Manual trigger");
        _pipelineRunRepository.Setup(r => r.GetByIdAsync(pipelineRun.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pipelineRun);

        var result = await _sut.StartAsync(pipelineRun.Id);

        Assert.Equal(PipelineRunStatus.Running, result.Status);
    }

    [Fact]
    public async Task StartAsync_WhenPipelineRunDoesNotExist_ThrowsNotFoundException()
    {
        _pipelineRunRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((PipelineRun?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.StartAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task CompleteAsync_WhenPipelineRunIsRunning_SetsStatusToSucceeded()
    {
        var pipelineRun = PipelineRun.Create(Guid.NewGuid(), null, "Manual trigger");
        pipelineRun.Start();
        _pipelineRunRepository.Setup(r => r.GetByIdAsync(pipelineRun.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pipelineRun);

        var result = await _sut.CompleteAsync(pipelineRun.Id, new CompletePipelineRunRequest(true, "All good"));

        Assert.Equal(PipelineRunStatus.Succeeded, result.Status);
        Assert.Equal("All good", result.LogOutput);
    }
}
