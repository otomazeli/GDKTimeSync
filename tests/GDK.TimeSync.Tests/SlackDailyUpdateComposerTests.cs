using GDK.TimeSync.Core;
using GDK.TimeSync.Slack;

namespace GDK.TimeSync.Tests;

public sealed class SlackDailyUpdateComposerTests
{
    private readonly SlackDailyUpdateComposer composer = new();

    [Fact]
    public void Compose_UsesIssueDescriptionAndBoldStatus()
    {
        var update = composer.Compose(new DateOnly(2026, 8, 13), [new SlackDailyCompletedItem("CGMFRAVII-2767", "Knowledge transfer", WorkStatus.Done)]);

        Assert.Equal("CGMFRAVII-2767 Knowledge transfer | *Done*", update!.SlackExtraLines);
        Assert.Equal("", update.SlackTitle);
        Assert.Equal("", update.SlackTaskHeading);
        Assert.Equal("", update.SlackUser);
    }

    [Fact]
    public void Compose_UsesTheSpecifiedDisplayNamesForEveryWorkStatus()
    {
        var update = composer.Compose(new DateOnly(2026, 8, 13), [
            new("CGM-1", "Review", WorkStatus.CodeReview),
            new("CGM-2", "Analysis", WorkStatus.Analyzing),
            new("CGM-3", "Complete", WorkStatus.Done),
            new("CGM-4", "Build", WorkStatus.InProgress),
            new("CGM-5", "Blocked", WorkStatus.Waiting)]);

        Assert.Equal("""
            CGM-1 Review | *Code review*
            CGM-2 Analysis | *Analyzing*
            CGM-3 Complete | *Done*
            CGM-4 Build | *In Progress*
            CGM-5 Blocked | *Waiting*
            """.ReplaceLineEndings("\n"), update!.SlackExtraLines);
    }

    [Fact]
    public void Compose_PutsTitleAndHeadingInTheirOwnFieldsAndTaskLinesAfterExtraLines()
    {
        var update = composer.Compose(
            new DateOnly(2026, 8, 13),
            [new("CGM-1", "Build", WorkStatus.InProgress)],
            new SlackDailyUpdateOptions("Daily update", "Completed work", ["", "  ", "Follow up tomorrow"], "planner"));

        Assert.Equal("Daily update", update!.SlackTitle);
        Assert.Equal("Completed work", update.SlackTaskHeading);
        Assert.Equal("planner", update.SlackUser);
        Assert.Equal("""
            Follow up tomorrow
            CGM-1 Build | *In Progress*
            """.ReplaceLineEndings("\n"), update.SlackExtraLines);
    }

    [Fact]
    public void Compose_MarksAnItemNotPostedToJiraWithoutOmittingItFromTheDigest()
    {
        var update = composer.Compose(new DateOnly(2026, 8, 13), [
            new("CGM-1", "Delivered", WorkStatus.Done, PostedToJira: true),
            new("CGM-2", "Not yet delivered", WorkStatus.InProgress, PostedToJira: false)]);

        Assert.Equal("""
            CGM-1 Delivered | *Done*
            CGM-2 Not yet delivered | *In Progress* (not posted in Jira)
            """.ReplaceLineEndings("\n"), update!.SlackExtraLines);
    }

    [Fact]
    public void Compose_ReturnsNullWhenThereAreNoCompletedItems()
    {
        var update = composer.Compose(new DateOnly(2026, 8, 13), []);

        Assert.Null(update);
    }
}
