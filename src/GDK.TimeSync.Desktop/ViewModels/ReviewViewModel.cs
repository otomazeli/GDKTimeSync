using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GDK.TimeSync.Core;

namespace GDK.TimeSync.Desktop.ViewModels;

public sealed class ReviewViewModel : INotifyPropertyChanged
{
    private readonly Func<DailyPlan>? planProvider;
    private string dryRunSummary = "Run Dry Run to validate the current local plan.";
    private bool isConfirmationVisible;

    public ReviewViewModel(Func<DailyPlan>? planProvider = null)
    {
        this.planProvider = planProvider;
        DryRunCommand = new RelayCommand(_ => RunDryRun());
        ConfirmReviewCommand = new RelayCommand(_ => IsConfirmationVisible = true);
        PostAllCommand = new RelayCommand(() => { }, () => false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool CanPostAll => false;
    public string PostAllExplanation => "Post all is disabled until the delivery workflow is available. No external systems can be contacted from this milestone.";
    public RelayCommand PostAllCommand { get; }
    public RelayCommand DryRunCommand { get; }
    public RelayCommand ConfirmReviewCommand { get; }
    public ObservableCollection<string> DryRunBlockers { get; } = [];
    public string DryRunSummary { get => dryRunSummary; private set => SetField(ref dryRunSummary, value); }
    public bool IsConfirmationVisible { get => isConfirmationVisible; private set => SetField(ref isConfirmationVisible, value); }

    private void RunDryRun()
    {
        DryRunBlockers.Clear();
        var plan = planProvider?.Invoke();
        if (plan is null)
        {
            DryRunBlockers.Add("No local plan is available to review.");
            DryRunSummary = "Dry Run found 1 blocker.";
            return;
        }

        foreach (var item in plan.Items)
        {
            if (string.IsNullOrWhiteSpace(item.JiraIssueKey))
                DryRunBlockers.Add("Each planned item needs a Jira issue key.");
            if (item.Duration <= TimeSpan.Zero)
                DryRunBlockers.Add("Each planned item needs a positive duration.");
            if (item.Start is { } start && item.End is { } end && end <= start)
                DryRunBlockers.Add("Planned item end times must be after start times.");
        }

        var duration = TimeSpan.FromTicks(plan.Items.Sum(item => item.Duration.Ticks));
        DryRunSummary = $"{plan.Items.Count} planned item(s), {duration.TotalMinutes:0} planned minute(s). Delivery sequence: Toggl → Jira → Tempo → Slack.";
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
