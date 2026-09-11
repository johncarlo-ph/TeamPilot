using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using TeamPilot.Domain.Exceptions;
using Xunit;

namespace TeamPilot.Domain.Tests;

public class PipelineRunTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();

    [Fact]
    public void Create_WithValidArguments_SetsStatusToQueued()
    {
        var pipelineRun = PipelineRun.Create(ProjectId, Guid.NewGuid(), "Merge of ticket 'Fix bug'");

        Assert.Equal(PipelineRunStatus.Queued, pipelineRun.Status);
    }

    [Fact]
    public void Start_WhenStatusIsQueued_SetsStatusToRunning()
    {
        var pipelineRun = PipelineRun.Create(ProjectId, null, "Manual trigger");

        pipelineRun.Start();

        Assert.Equal(PipelineRunStatus.Running, pipelineRun.Status);
        Assert.NotNull(pipelineRun.StartedAtUtc);
    }

    [Fact]
    public void Start_WhenAlreadyRunning_ThrowsInvalidPipelineRunStateTransitionException()
    {
        var pipelineRun = PipelineRun.Create(ProjectId, null, "Manual trigger");
        pipelineRun.Start();

        Assert.Throws<InvalidPipelineRunStateTransitionException>(() => pipelineRun.Start());
    }

    [Fact]
    public void Complete_WhenStatusIsRunning_SetsStatusToSucceededOrFailed()
    {
        var pipelineRun = PipelineRun.Create(ProjectId, null, "Manual trigger");
        pipelineRun.Start();

        pipelineRun.Complete(succeeded: true, logOutput: "All tests passed");

        Assert.Equal(PipelineRunStatus.Succeeded, pipelineRun.Status);
        Assert.Equal("All tests passed", pipelineRun.LogOutput);
        Assert.NotNull(pipelineRun.CompletedAtUtc);
    }

    [Fact]
    public void Complete_WhenStatusIsQueued_ThrowsInvalidPipelineRunStateTransitionException()
    {
        var pipelineRun = PipelineRun.Create(ProjectId, null, "Manual trigger");

        Assert.Throws<InvalidPipelineRunStateTransitionException>(() => pipelineRun.Complete(true, null));
    }
}
