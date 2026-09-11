using TeamPilot.Domain.Entities;
using TeamPilot.Domain.Enums;
using TeamPilot.Domain.Exceptions;
using Xunit;

namespace TeamPilot.Domain.Tests;

public class ConflictTests
{
    [Fact]
    public void RecordAiSuggestion_SetsStatusToAiResolutionSuggested()
    {
        var conflict = Conflict.Create(Guid.NewGuid(), "src/App.cs", "<<<<<<<");

        conflict.RecordAiSuggestion("Use theirs");

        Assert.Equal(ConflictStatus.AiResolutionSuggested, conflict.Status);
        Assert.Equal("Use theirs", conflict.AiSuggestedResolution);
    }

    [Fact]
    public void AcceptAiSuggestion_WithoutPriorSuggestion_ThrowsInvalidOperationException()
    {
        var conflict = Conflict.Create(Guid.NewGuid(), "src/App.cs", "<<<<<<<");

        Assert.Throws<InvalidOperationException>(() => conflict.AcceptAiSuggestion("Alice"));
    }

    [Fact]
    public void AcceptAiSuggestion_WithPriorSuggestion_SetsStatusToResolvedWithAiSuggestion()
    {
        var conflict = Conflict.Create(Guid.NewGuid(), "src/App.cs", "<<<<<<<");
        conflict.RecordAiSuggestion("Use theirs");

        conflict.AcceptAiSuggestion("Alice");

        Assert.Equal(ConflictStatus.ResolvedWithAiSuggestion, conflict.Status);
        Assert.Equal("Use theirs", conflict.ResolutionNote);
    }

    [Fact]
    public void ResolveManually_WhenAlreadyResolved_ThrowsInvalidConflictStateTransitionException()
    {
        var conflict = Conflict.Create(Guid.NewGuid(), "src/App.cs", "<<<<<<<");
        conflict.ResolveManually("Kept ours", "Alice");

        Assert.Throws<InvalidConflictStateTransitionException>(() => conflict.ResolveManually("Kept theirs", "Bob"));
    }
}
