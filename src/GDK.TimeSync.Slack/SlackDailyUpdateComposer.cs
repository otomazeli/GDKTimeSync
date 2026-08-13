using GDK.TimeSync.Core;

namespace GDK.TimeSync.Slack;

public sealed class SlackDailyUpdateComposer
{
    public SlackDailyUpdate? Compose(
        DateOnly date,
        IReadOnlyList<SlackDailyCompletedItem> completedItems,
        SlackDailyUpdateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(completedItems);
        if (completedItems.Count == 0)
            return null;

        var lines = new List<string>();
        AddIfPresent(lines, options?.Title);
        AddIfPresent(lines, options?.Header);
        if (options?.ExtraLines is not null)
            foreach (var extraLine in options.ExtraLines)
                AddIfPresent(lines, extraLine);

        foreach (var item in completedItems)
        {
            ArgumentNullException.ThrowIfNull(item);
            lines.Add($"{item.TogglProject} | {item.JiraIssueKey} {item.Description} | *{DisplayName(item.Status)}*");
        }

        return new SlackDailyUpdate(date, string.Join("\n", lines));
    }

    private static void AddIfPresent(ICollection<string> lines, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            lines.Add(value);
    }

    private static string DisplayName(WorkStatus status) => status switch
    {
        WorkStatus.CodeReview => "Code review",
        WorkStatus.Analyzing => "Analyzing",
        WorkStatus.Done => "Done",
        WorkStatus.InProgress => "In Progress",
        WorkStatus.Waiting => "Waiting",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
}
