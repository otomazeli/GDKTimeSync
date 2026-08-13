using GDK.TimeSync.Core;
using GDK.TimeSync.Slack;

namespace GDK.TimeSync.Tests;

public sealed class SlackDailyUpdateComposerTests
{
    private readonly SlackDailyUpdateComposer composer = new();

    [Fact]
    public void Compose_UsesProjectIssueDescriptionAndBoldStatus()
    {
        var update = composer.Compose(new DateOnly(2026, 8, 13), [new SlackDailyCompletedItem("CGM", "CGMFRAVII-2767", "Knowledge transfer", WorkStatus.Done)]);

        Assert.Equal("CGM | CGMFRAVII-2767 Knowledge transfer | *Done*", update!.Text);
    }

    [Fact]
    public void Compose_UsesTheSpecifiedDisplayNamesForEveryWorkStatus()
    {
        var update = composer.Compose(new DateOnly(2026, 8, 13), [
            new("CGM", "CGM-1", "Review", WorkStatus.CodeReview),
            new("CGM", "CGM-2", "Analysis", WorkStatus.Analyzing),
            new("CGM", "CGM-3", "Complete", WorkStatus.Done),
            new("CGM", "CGM-4", "Build", WorkStatus.InProgress),
            new("CGM", "CGM-5", "Blocked", WorkStatus.Waiting)]);

        Assert.Equal("""
            CGM | CGM-1 Review | *Code review*
            CGM | CGM-2 Analysis | *Analyzing*
            CGM | CGM-3 Complete | *Done*
            CGM | CGM-4 Build | *In Progress*
            CGM | CGM-5 Blocked | *Waiting*
            """, update!.Text);
    }

    [Fact]
    public void Compose_ExcludesBlankOptionalLines()
    {
        var update = composer.Compose(
            new DateOnly(2026, 8, 13),
            [new("CGM", "CGM-1", "Build", WorkStatus.InProgress)],
            new SlackDailyUpdateOptions("Daily update", "Completed work", ["", "  ", "Follow up tomorrow"]));

        Assert.Equal("""
            Daily update
            Completed work
            Follow up tomorrow
            CGM | CGM-1 Build | *In Progress*
            """, update!.Text);
    }

    [Fact]
    public void Compose_ReturnsNullWhenThereAreNoCompletedItems()
    {
        var update = composer.Compose(new DateOnly(2026, 8, 13), []);

        Assert.Null(update);
    }
}
