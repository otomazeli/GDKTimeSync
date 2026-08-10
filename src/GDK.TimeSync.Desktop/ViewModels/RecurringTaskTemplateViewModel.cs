namespace GDK.TimeSync.Desktop.ViewModels;

public sealed class RecurringTaskTemplateViewModel(
    string name,
    string jiraIssueKey,
    string description,
    TimeSpan duration,
    string togglProject,
    string tempoCategory)
{
    public string Name { get; } = name;
    public string JiraIssueKey { get; } = jiraIssueKey;
    public string Description { get; } = description;
    public TimeSpan Duration { get; } = duration;
    public string TogglProject { get; } = togglProject;
    public string TempoCategory { get; } = tempoCategory;
}
