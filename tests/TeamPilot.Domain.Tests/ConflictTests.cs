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
        Assert.Equal("Use theirs", conflict.ResolvedContent);
        Assert.Null(conflict.ResolutionNote);
    }

    [Fact]
    public void ResolveManually_WithContentAndNote_SetsResolvedContentAndNote()
    {
        var conflict = Conflict.Create(Guid.NewGuid(), "src/App.cs", "<<<<<<<");

        conflict.ResolveManually("final merged content", "Kept both changes", "Alice");

        Assert.Equal(ConflictStatus.ResolvedManually, conflict.Status);
        Assert.Equal("final merged content", conflict.ResolvedContent);
        Assert.Equal("Kept both changes", conflict.ResolutionNote);
    }

    [Fact]
    public void ResolveManually_WhenAlreadyResolved_ThrowsInvalidConflictStateTransitionException()
    {
        var conflict = Conflict.Create(Guid.NewGuid(), "src/App.cs", "<<<<<<<");
        conflict.ResolveManually("Kept ours", null, "Alice");

        Assert.Throws<InvalidConflictStateTransitionException>(() => conflict.ResolveManually("Kept theirs", null, "Bob"));
    }

    [Fact]
    public void Create_WithBaseTipSha_StampsItOnTheConflict()
    {
        var conflict = Conflict.Create(Guid.NewGuid(), "src/App.cs", "<<<<<<<", baseTipSha: "sha-abc123");

        Assert.Equal("sha-abc123", conflict.BaseTipSha);
    }

    [Fact]
    public void MarkStale_AfterManualResolution_ResetsToDetectedAndClearsResolutionFieldsButKeepsBaseTipSha()
    {
        var conflict = Conflict.Create(Guid.NewGuid(), "src/App.cs", "<<<<<<<", baseTipSha: "sha-old");
        conflict.ResolveManually("final merged content", "Kept both changes", "Alice");

        conflict.MarkStale();

        Assert.Equal(ConflictStatus.Detected, conflict.Status);
        Assert.Null(conflict.ResolvedContent);
        Assert.Null(conflict.ResolutionNote);
        Assert.Null(conflict.ResolvedBy);
        Assert.Null(conflict.ResolvedAtUtc);
        Assert.Equal("sha-old", conflict.BaseTipSha);
    }

    [Fact]
    public void MarkStale_AfterAiSuggestionAccepted_KeepsTheAiSuggestionForReuse()
    {
        var conflict = Conflict.Create(Guid.NewGuid(), "src/App.cs", "<<<<<<<", baseTipSha: "sha-old");
        conflict.RecordAiSuggestion("Use theirs");
        conflict.AcceptAiSuggestion("Alice");

        conflict.MarkStale();

        Assert.Equal(ConflictStatus.Detected, conflict.Status);
        Assert.Null(conflict.ResolvedContent);
        Assert.Equal("Use theirs", conflict.AiSuggestedResolution);
    }

    [Fact]
    public void MarkStale_AllowsResolvingAgainAfterward()
    {
        var conflict = Conflict.Create(Guid.NewGuid(), "src/App.cs", "<<<<<<<", baseTipSha: "sha-old");
        conflict.ResolveManually("first attempt", null, "Alice");
        conflict.MarkStale();

        conflict.ResolveManually("second attempt", null, "Alice");

        Assert.Equal(ConflictStatus.ResolvedManually, conflict.Status);
        Assert.Equal("second attempt", conflict.ResolvedContent);
    }
}
