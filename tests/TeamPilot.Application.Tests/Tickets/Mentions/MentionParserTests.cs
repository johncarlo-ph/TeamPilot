using TeamPilot.Application.Tickets.Mentions;
using Xunit;

namespace TeamPilot.Application.Tests.Tickets.Mentions;

public class MentionParserTests
{
    [Fact]
    public void Parse_NullDescription_ReturnsEmpty()
    {
        Assert.Empty(MentionParser.Parse(null));
    }

    [Fact]
    public void Parse_NoMentions_ReturnsEmpty()
    {
        Assert.Empty(MentionParser.Parse("Just a plain description with no mentions."));
    }

    [Fact]
    public void Parse_SingleTicketMention_ReturnsOneMention()
    {
        var id = Guid.NewGuid();
        var mentions = MentionParser.Parse($"See @[Fix login bug](ticket:{id}) for context.");

        var mention = Assert.Single(mentions);
        Assert.Equal(MentionType.Ticket, mention.Type);
        Assert.Equal(id, mention.Id);
        Assert.Equal("Fix login bug", mention.Label);
    }

    [Fact]
    public void Parse_SingleProjectMention_ReturnsOneMention()
    {
        var id = Guid.NewGuid();
        var mentions = MentionParser.Parse($"Depends on @[Billing Service](project:{id}).");

        var mention = Assert.Single(mentions);
        Assert.Equal(MentionType.Project, mention.Type);
        Assert.Equal(id, mention.Id);
        Assert.Equal("Billing Service", mention.Label);
    }

    [Fact]
    public void Parse_MultipleMentions_ReturnsAllInOrder()
    {
        var ticketId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var description = $"See @[Ticket A](ticket:{ticketId}) and @[Project B](project:{projectId}).";

        var mentions = MentionParser.Parse(description);

        Assert.Equal(2, mentions.Count);
        Assert.Equal(MentionType.Ticket, mentions[0].Type);
        Assert.Equal(ticketId, mentions[0].Id);
        Assert.Equal(MentionType.Project, mentions[1].Type);
        Assert.Equal(projectId, mentions[1].Id);
    }

    [Fact]
    public void Parse_MalformedToken_IsIgnored()
    {
        Assert.Empty(MentionParser.Parse("Not a real mention: @[Label](ticket:not-a-guid)"));
        Assert.Empty(MentionParser.Parse("Missing parens: @[Label]ticket:" + Guid.NewGuid()));
        Assert.Empty(MentionParser.Parse("Unknown type: @[Label](sprint:" + Guid.NewGuid() + ")"));
    }

    [Fact]
    public void Parse_DuplicateMentionOfSameId_ReturnsBothOccurrences()
    {
        var id = Guid.NewGuid();
        var description = $"@[First mention](ticket:{id}) ... later again @[First mention](ticket:{id})";

        var mentions = MentionParser.Parse(description);

        Assert.Equal(2, mentions.Count);
    }
}
