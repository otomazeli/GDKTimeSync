using System.ComponentModel;
using System.Runtime.CompilerServices;
using GDK.TimeSync.Core;

namespace GDK.TimeSync.Desktop.ViewModels;

public sealed class RecurringTaskTemplateViewModel : INotifyPropertyChanged
{
    private string name;
    private string jiraIssueKey;
    private string description;
    private TimeSpan duration;
    private string togglProject;
    private string tempoCategory;
    private WorkStatus status;
    private long? togglProjectId;

    public RecurringTaskTemplateViewModel(
        string name = "",
        string jiraIssueKey = "",
        string description = "",
        TimeSpan? duration = null,
        string togglProject = "",
        string tempoCategory = "",
        bool isBillable = true,
        Guid? id = null,
        WorkStatus status = WorkStatus.InProgress,
        long? togglProjectId = null)
    {
        Id = id ?? Guid.NewGuid();
        this.name = name;
        this.jiraIssueKey = jiraIssueKey;
        this.description = description;
        this.duration = duration ?? TimeSpan.Zero;
        this.togglProject = togglProject;
        this.tempoCategory = tempoCategory;
        IsBillable = isBillable;
        this.status = status;
        this.togglProjectId = togglProjectId;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; }
    public string Name { get => name; set => SetField(ref name, value); }
    public string JiraIssueKey { get => jiraIssueKey; set => SetField(ref jiraIssueKey, value); }
    public string Description { get => description; set => SetField(ref description, value); }
    public TimeSpan Duration { get => duration; set => SetField(ref duration, value); }
    public string TogglProject { get => togglProject; set => SetField(ref togglProject, value); }
    public string TempoCategory { get => tempoCategory; set => SetField(ref tempoCategory, value); }
    public bool IsBillable { get; }
    public WorkStatus Status { get => status; set => SetField(ref status, value); }
    public long? TogglProjectId { get => togglProjectId; set => SetField(ref togglProjectId, value); }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
