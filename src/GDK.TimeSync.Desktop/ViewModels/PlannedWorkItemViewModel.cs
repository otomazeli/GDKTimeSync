using System.ComponentModel;
using System.Runtime.CompilerServices;
using GDK.TimeSync.Core;
using GDK.TimeSync.Toggl;

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
    private long? togglEntryId;
    private ItemSource source;
    private IReadOnlyList<TogglProject> togglProjectOptions = [];

    public PlannedWorkItemViewModel(string name = "", string jiraIssueKey = "", string description = "", TimeSpan? duration = null, string togglProject = "", string tempoCategory = "", Guid? id = null, TimeOnly? start = null, TimeOnly? end = null, bool isBillable = true, WorkStatus status = WorkStatus.InProgress, long? togglProjectId = null, bool postToToggl = true, long? togglEntryId = null, ItemSource source = ItemSource.Local)
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
        this.togglEntryId = togglEntryId;
        this.source = source;
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
    public long? TogglProjectId
    {
        get => togglProjectId;
        set
        {
            if (SetField(ref togglProjectId, value)) OnPropertyChanged(nameof(SelectedTogglProject));
        }
    }

    // The project picker binds ItemsSource/SelectedItem to these two rather than
    // SelectedValue/SelectedValuePath over a list reached by RelativeSource. That old shape wrote
    // null straight back into TogglProjectId whenever the ComboBox could not resolve the id --
    // which happens on any cell realised before the list is there -- and autosave persisted it.
    // The name survived because nothing was bound to it, which is how rows ended up with a project
    // name and no id, and posted to Toggl with no project at all.
    public IReadOnlyList<TogglProject> TogglProjectOptions
    {
        get => togglProjectOptions;
        private set => SetField(ref togglProjectOptions, value);
    }

    public TogglProject? SelectedTogglProject
    {
        get => TogglProjectOptions.FirstOrDefault(project => project.Id == TogglProjectId);
        set
        {
            // A null with nothing to choose from is the control failing to resolve, not the user
            // clearing the field. Only an empty selection made against real options is a real one.
            if (value is null && TogglProjectOptions.Count == 0) return;

            TogglProjectId = value?.Id;
            TogglProject = value?.Name ?? "";
            OnPropertyChanged();
        }
    }

    /// <returns>true if a wiped id was recovered, so the caller knows the row needs saving.</returns>
    public bool SetTogglProjectOptions(IReadOnlyList<TogglProject> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        TogglProjectOptions = options;

        // Recover a row whose id was wiped by the old binding: the name is still there, so the id
        // can be matched back. A name that matches nothing is left alone rather than guessed at.
        var repaired = false;
        if (TogglProjectId is null && !string.IsNullOrWhiteSpace(TogglProject))
        {
            TogglProjectId = options.FirstOrDefault(project =>
                string.Equals(project.Name, TogglProject, StringComparison.OrdinalIgnoreCase))?.Id;
            repaired = TogglProjectId is not null;
        }

        OnPropertyChanged(nameof(SelectedTogglProject));
        return repaired;
    }
    public bool PostToToggl { get => postToToggl; set => SetField(ref postToToggl, value); }
    public long? TogglEntryId { get => togglEntryId; set => SetField(ref togglEntryId, value); }
    public ItemSource Source { get => source; set => SetField(ref source, value); }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void RecalculateDuration()
    {
        if (start is not { } from || end is not { } to || to == from) return;
        var value = PlannedWorkItem.ComputeSpan(from, to);
        if (duration == value) return;
        duration = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Duration)));
    }
}
