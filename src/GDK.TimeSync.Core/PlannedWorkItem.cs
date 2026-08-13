namespace GDK.TimeSync.Core;

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
}
