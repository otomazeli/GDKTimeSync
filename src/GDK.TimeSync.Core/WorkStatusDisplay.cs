namespace GDK.TimeSync.Core;

// One place for how a WorkStatus is shown. The name and the emoji were previously spread across
// SlackDailyUpdateComposer and WorkStatusOption, so adding an icon would have meant a third copy.
public static class WorkStatusDisplay
{
    public static string Emoji(WorkStatus status) => status switch
    {
        WorkStatus.InProgress => "\U0001F504",  // 🔄
        WorkStatus.CodeReview => "\U0001F440",  // 👀
        WorkStatus.Analyzing => "\U0001F50D",   // 🔍
        WorkStatus.Done => "✅",            // ✅
        WorkStatus.Waiting => "⏳",         // ⏳
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    public static string Name(WorkStatus status) => status switch
    {
        WorkStatus.InProgress => "In Progress",
        WorkStatus.CodeReview => "Code review",
        WorkStatus.Analyzing => "Analyzing",
        WorkStatus.Done => "Done",
        WorkStatus.Waiting => "Waiting",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    // Emoji plus word, for anywhere a human is choosing rather than skimming.
    public static string Label(WorkStatus status) => $"{Emoji(status)} {Name(status)}";
}
