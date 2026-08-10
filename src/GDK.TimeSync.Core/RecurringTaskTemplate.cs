namespace GDK.TimeSync.Core;

public sealed record RecurringTaskTemplate(
    Guid Id,
    string Name,
    string JiraIssueKey,
    string Description,
    TimeSpan Duration,
    string TogglProject,
    string TempoCategory,
    bool IsBillable = true)
{
    public static RecurringTaskTemplate Create(
        string name,
        string jiraIssueKey,
        string description,
        TimeSpan duration,
        string togglProject,
        string tempoCategory,
        bool isBillable = true) =>
        new(Guid.NewGuid(), name, jiraIssueKey, description, duration, togglProject, tempoCategory, isBillable);
}
