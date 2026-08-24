using System.ComponentModel;
using System.Runtime.CompilerServices;
using GDK.TimeSync.Core;

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
    private WorkStatus status;
    private long? togglProjectId;
    private bool postToToggl;

    public PlannedWorkItemViewModel(string name = "", string jiraIssueKey = "", string description = "", TimeSpan? duration = null, string togglProject = "", string tempoCategory = "", Guid? id = null, TimeOnly? start = null, TimeOnly? end = null, bool isBillable = true, WorkStatus status = WorkStatus.InProgress, long? togglProjectId = null, bool postToToggl = true)
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
        this.status = status;
        this.togglProjectId = togglProjectId;
        this.postToToggl = postToToggl;
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
    public TimeOnly? Start { get => start; set { if (SetField(ref start, value)) RecalculateDuration(); } }
    public TimeOnly? End { get => end; set { if (SetField(ref end, value)) RecalculateDuration(); } }
    public bool IsBillable { get => isBillable; set => SetField(ref isBillable, value); }
    public WorkStatus Status { get => status; set => SetField(ref status, value); }
    public long? TogglProjectId { get => togglProjectId; set => SetField(ref togglProjectId, value); }
    public bool PostToToggl { get => postToToggl; set => SetField(ref postToToggl, value); }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void RecalculateDuration()
    {
        if (start is not { } from || end is not { } to || to <= from) return;
        var value = to - from;
        if (duration == value) return;
        duration = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Duration)));
    }
}
