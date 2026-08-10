using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GDK.TimeSync.Desktop.ViewModels;

public sealed class PlannedWorkItemViewModel : INotifyPropertyChanged
{
    private string name;
    private string jiraIssueKey;
    private string description;
    private TimeSpan duration;
    private string togglProject;
    private string tempoCategory;

    public PlannedWorkItemViewModel(string name = "", string jiraIssueKey = "", string description = "", TimeSpan? duration = null, string togglProject = "", string tempoCategory = "")
    {
        this.name = name;
        this.jiraIssueKey = jiraIssueKey;
        this.description = description;
        this.duration = duration ?? TimeSpan.Zero;
        this.togglProject = togglProject;
        this.tempoCategory = tempoCategory;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsEditable => true;

    public string Name { get => name; set => SetField(ref name, value); }
    public string JiraIssueKey { get => jiraIssueKey; set => SetField(ref jiraIssueKey, value); }
    public string Description { get => description; set => SetField(ref description, value); }
    public TimeSpan Duration { get => duration; set => SetField(ref duration, value); }
    public string TogglProject { get => togglProject; set => SetField(ref togglProject, value); }
    public string TempoCategory { get => tempoCategory; set => SetField(ref tempoCategory, value); }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
