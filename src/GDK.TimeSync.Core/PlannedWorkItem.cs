namespace GDK.TimeSync.Core;

public enum ItemSource { Local, Toggl }

public sealed record PlannedWorkItem(
    Guid Id,
    DateOnly Day,
    TimeOnly? Start,
    TimeOnly? End,
    string Name,
    string JiraIssueKey,
    string Comment,
    TimeSpan Duration,
    string TogglProject,
    string TempoCategory,
    bool IsBillable,
    WorkStatus Status = WorkStatus.InProgress)
{
    // What this item is called in Toggl. The description is the only field that carries the Jira key
    // across the round trip -- TogglSyncService.ParseDescription reads "KEY - Comment" back off an
    // imported entry to recover it -- so writing the bare comment meant an entry this app created
    // came back with no key attached.
    public string TogglDescription
    {
        get
        {
            var comment = Comment?.Trim() ?? "";
            var key = JiraIssueKey?.Trim() ?? "";
            if (key.Length == 0) return comment;
            if (comment.Length == 0) return key;
            return comment.StartsWith(key, StringComparison.OrdinalIgnoreCase) ? comment : $"{key} - {comment}";
        }
    }

    public long? TogglProjectId { get; init; }
    public bool PostToToggl { get; init; } = true;
    public long? TogglEntryId { get; init; }
    public ItemSource Source { get; init; } = ItemSource.Local;

    public static PlannedWorkItem Create(
        DateOnly day,
        string name = "",
        string jiraIssueKey = "",
        string comment = "",
        TimeSpan? duration = null,
        string togglProject = "",
        string tempoCategory = "",
        bool isBillable = true,
        TimeOnly? start = null,
        TimeOnly? end = null) =>
        new(Guid.NewGuid(), day, start, end, name, jiraIssueKey, comment, duration ?? TimeSpan.Zero, togglProject, tempoCategory, isBillable, WorkStatus.InProgress);

    // An end time at or before start means the task runs past midnight into the next day
    // (e.g. 23:30 -> 00:15), not a zero/negative-length task.
    public static bool EndWrapsToNextDay(TimeOnly start, TimeOnly end) => end < start;

    public static TimeSpan ComputeSpan(TimeOnly start, TimeOnly end)
    {
        var span = end.ToTimeSpan() - start.ToTimeSpan();
        return EndWrapsToNextDay(start, end) ? span + TimeSpan.FromHours(24) : span;
    }
}
