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
        if (options?.ExtraLines is not null)
            foreach (var extraLine in options.ExtraLines)
                AddIfPresent(lines, extraLine);

        foreach (var item in completedItems)
        {
            ArgumentNullException.ThrowIfNull(item);
            // The Jira mark is always present: a reader skimming the channel should never have to
            // notice something absent to know a task has not reached Jira yet.
            lines.Add($"{item.JiraIssueKey} {item.Description} | {WorkStatusDisplay.Emoji(item.Status)} {JiraMark(item.PostedToJira)}");
        }

        return new SlackDailyUpdate(date, options?.Title ?? "", options?.Header ?? "", string.Join("\n", lines), options?.JiraUser ?? "");
    }

    private static void AddIfPresent(ICollection<string> lines, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            lines.Add(value);
    }

    private static string JiraMark(bool postedToJira) => postedToJira ? "🔷" : "⚪";
}
