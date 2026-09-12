using TeamPilot.Domain.Entities;
using Xunit;

namespace TeamPilot.Domain.Tests;

public class WorkflowStageTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid AgentId = Guid.NewGuid();

    [Fact]
    public void Create_WithEmptyProjectId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => WorkflowStage.Create(Guid.Empty, AgentId, 0));
    }

    [Fact]
    public void Create_WithEmptyAgentId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => WorkflowStage.Create(ProjectId, Guid.Empty, 0));
    }

    [Fact]
    public void Create_WithNegativeOrder_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => WorkflowStage.Create(ProjectId, AgentId, -1));
    }

    [Fact]
    public void Create_WithValidArguments_SetsOrderAndNoLoopBack()
    {
        var stage = WorkflowStage.Create(ProjectId, AgentId, 2);

        Assert.Equal(2, stage.Order);
        Assert.Null(stage.LoopBackToStageId);
        Assert.Null(stage.MaxLoopIterations);
    }

    [Fact]
    public void MoveTo_WithNegativeOrder_ThrowsArgumentException()
    {
        var stage = WorkflowStage.Create(ProjectId, AgentId, 0);

        Assert.Throws<ArgumentException>(() => stage.MoveTo(-1));
    }

    [Fact]
    public void MoveTo_WithValidOrder_UpdatesOrder()
    {
        var stage = WorkflowStage.Create(ProjectId, AgentId, 0);

        stage.MoveTo(3);

        Assert.Equal(3, stage.Order);
    }

    [Fact]
    public void SetLoopBack_ToItself_ThrowsArgumentException()
    {
        var stage = WorkflowStage.Create(ProjectId, AgentId, 1);

        Assert.Throws<ArgumentException>(() => stage.SetLoopBack(stage.Id, 3));
    }

    [Fact]
    public void SetLoopBack_WithEmptyTargetId_ThrowsArgumentException()
    {
        var stage = WorkflowStage.Create(ProjectId, AgentId, 1);

        Assert.Throws<ArgumentException>(() => stage.SetLoopBack(Guid.Empty, 3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SetLoopBack_WithNonPositiveMaxIterations_ThrowsArgumentException(int maxIterations)
    {
        var stage = WorkflowStage.Create(ProjectId, AgentId, 1);

        Assert.Throws<ArgumentException>(() => stage.SetLoopBack(Guid.NewGuid(), maxIterations));
    }

    [Fact]
    public void SetLoopBack_WithValidTarget_SetsTargetAndMaxIterations()
    {
        var stage = WorkflowStage.Create(ProjectId, AgentId, 1);
        var targetId = Guid.NewGuid();

        stage.SetLoopBack(targetId, 3);

        Assert.Equal(targetId, stage.LoopBackToStageId);
        Assert.Equal(3, stage.MaxLoopIterations);
    }

    [Fact]
    public void ClearLoopBack_AfterSet_RemovesTargetAndMaxIterations()
    {
        var stage = WorkflowStage.Create(ProjectId, AgentId, 1);
        stage.SetLoopBack(Guid.NewGuid(), 3);

        stage.ClearLoopBack();

        Assert.Null(stage.LoopBackToStageId);
        Assert.Null(stage.MaxLoopIterations);
    }
}
