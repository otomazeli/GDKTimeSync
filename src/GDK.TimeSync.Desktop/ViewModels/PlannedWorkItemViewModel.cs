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
    private TimeOnly? start;
    private TimeOnly? end;
    private bool isBillable;

    public PlannedWorkItemViewModel(string name = "", string jiraIssueKey = "", string description = "", TimeSpan? duration = null, string togglProject = "", string tempoCategory = "", Guid? id = null, TimeOnly? start = null, TimeOnly? end = null, bool isBillable = true)
    {
        Id = id ?? Guid.NewGuid();
        this.name = name;
        this.jiraIssueKey = jiraIssueKey;
        this.description = description;
        this.duration = duration ?? TimeSpan.Zero;
        this.togglProject = togglProject;
        this.tempoCategory = tempoCategory;
        this.start = start;
        this.end = end;
        this.isBillable = isBillable;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsEditable => true;
    public Guid Id { get; }

    public string Name { get => name; set => SetField(ref name, value); }
    public string JiraIssueKey { get => jiraIssueKey; set => SetField(ref jiraIssueKey, value); }
    public string Description { get => description; set => SetField(ref description, value); }
    public TimeSpan Duration { get => duration; set => SetField(ref duration, value); }
    public string TogglProject { get => togglProject; set => SetField(ref togglProject, value); }
    public string TempoCategory { get => tempoCategory; set => SetField(ref tempoCategory, value); }
    public TimeOnly? Start { get => start; set => SetField(ref start, value); }
    public TimeOnly? End { get => end; set => SetField(ref end, value); }
    public bool IsBillable { get => isBillable; set => SetField(ref isBillable, value); }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
