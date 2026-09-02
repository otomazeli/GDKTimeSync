using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.ViewModels;

namespace GDK.TimeSync.Tests;

public sealed class WorkStatusDisplayTests
{
    // The Slack path formats a status on every digest line, so a status added later without a mapping
    // would throw at the moment the user sends their daily update. Catch it here instead.
    [Fact]
    public void EveryWorkStatusHasAnEmojiAndAName()
    {
        foreach (var status in Enum.GetValues<WorkStatus>())
        {
            Assert.False(string.IsNullOrWhiteSpace(WorkStatusDisplay.Emoji(status)), $"{status} has no emoji");
            Assert.False(string.IsNullOrWhiteSpace(WorkStatusDisplay.Name(status)), $"{status} has no name");
        }
    }

    [Fact]
    public void EveryStatusIconIsDistinct()
    {
        var icons = Enum.GetValues<WorkStatus>().Select(WorkStatusDisplay.Emoji).ToArray();

        Assert.Equal(icons.Length, icons.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(WorkStatus.InProgress, "\U0001F504")]
    [InlineData(WorkStatus.Done, "✅")]
    [InlineData(WorkStatus.CodeReview, "\U0001F440")]
    [InlineData(WorkStatus.Analyzing, "\U0001F50D")]
    [InlineData(WorkStatus.Waiting, "⏳")]
    public void UsesTheAgreedIcon(WorkStatus status, string expected) =>
        Assert.Equal(expected, WorkStatusDisplay.Emoji(status));

    // The picker keeps the word: the icon is for skimming Slack, not for choosing in a dropdown.
    [Fact]
    public void TheStatusPickerShowsBothTheIconAndTheWord()
    {
        var options = WorkStatusOption.All;

        Assert.Equal(Enum.GetValues<WorkStatus>().Length, options.Count);
        var done = Assert.Single(options, option => option.Value == WorkStatus.Done);
        Assert.Equal("✅ Done", done.DisplayName);
        Assert.All(options, option => Assert.Contains(WorkStatusDisplay.Name(option.Value), option.DisplayName, StringComparison.Ordinal));
    }
}
