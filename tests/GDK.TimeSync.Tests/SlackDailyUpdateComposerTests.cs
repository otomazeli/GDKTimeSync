using GDK.TimeSync.Core;
using GDK.TimeSync.Slack;

namespace GDK.TimeSync.Tests;

public sealed class SlackDailyUpdateComposerTests
{
    private readonly SlackDailyUpdateComposer composer = new();

    [Fact]
    public void Compose_UsesIssueDescriptionAndStatusIcon()
    {
        var update = composer.Compose(new DateOnly(2026, 8, 13), [new SlackDailyCompletedItem("CGMFRAVII-2767", "Knowledge transfer", WorkStatus.Done)]);

        Assert.Equal("CGMFRAVII-2767 Knowledge transfer | ✅ 🔷", update!.SlackExtraLines);
        Assert.Equal("", update.SlackTitle);
        Assert.Equal("", update.SlackTaskHeading);
        Assert.Equal("", update.SlackUser);
    }

    [Fact]
    public void Compose_UsesTheSpecifiedIconForEveryWorkStatus()
    {
        var update = composer.Compose(new DateOnly(2026, 8, 13), [
            new("CGM-1", "Review", WorkStatus.CodeReview),
            new("CGM-2", "Analysis", WorkStatus.Analyzing),
            new("CGM-3", "Complete", WorkStatus.Done),
            new("CGM-4", "Build", WorkStatus.InProgress),
            new("CGM-5", "Blocked", WorkStatus.Waiting)]);

        Assert.Equal("""
            CGM-1 Review | 👀 🔷
            CGM-2 Analysis | 🔍 🔷
            CGM-3 Complete | ✅ 🔷
            CGM-4 Build | 🔄 🔷
            CGM-5 Blocked | ⏳ 🔷
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
            CGM-1 Build | 🔄 🔷
            """.ReplaceLineEndings("\n"), update.SlackExtraLines);
    }

    [Fact]
    public void Compose_MarksAnItemNotPostedToJiraWithItsOwnIconRatherThanAnAbsence()
    {
        var update = composer.Compose(new DateOnly(2026, 8, 13), [
            new("CGM-1", "Delivered", WorkStatus.Done, PostedToJira: true),
            new("CGM-2", "Not yet delivered", WorkStatus.InProgress, PostedToJira: false)]);

        Assert.Equal("""
            CGM-1 Delivered | ✅ 🔷
            CGM-2 Not yet delivered | 🔄 ⚪
            """.ReplaceLineEndings("\n"), update!.SlackExtraLines);
    }

    [Fact]
    public void Compose_ReturnsNullWhenThereAreNoCompletedItems()
    {
        var update = composer.Compose(new DateOnly(2026, 8, 13), []);

        Assert.Null(update);
    }
}
