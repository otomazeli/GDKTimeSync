using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Slack;

namespace GDK.TimeSync.Desktop.ViewModels;

public sealed class ReviewViewModel : INotifyPropertyChanged
{
    private readonly ILocalPlanSnapshotProvider? planProvider;
    private readonly IConfirmedTaskDeliveryService? deliveryService;
    private readonly IDeliveryAttemptRepository? attempts;
    private readonly IDailySlackDeliveryRepository? dailyDeliveries;
    private readonly ISlackClientFactory? slackClientFactory;
    private readonly IUserSettingsStore? settings;
    private string dryRunSummary = "Run Dry Run to validate the current local plan.";
    private PlannedWorkItem? selectedTask;
    private DeliveryAttempt? lastTaskAttempt;
    private SlackDailyUpdate? slackPreview;
    private string? taskDeliveryError;
    private string? slackDeliveryError;
    private bool isTaskConfirmationVisible;
    private bool isSlackConfirmationVisible;
    private bool canConfirmSlack;
    private bool isTaskDeliveryInFlight;

    public ReviewViewModel(
        ILocalPlanSnapshotProvider? planProvider = null,
        IConfirmedTaskDeliveryService? deliveryService = null,
        IDeliveryAttemptRepository? attempts = null,
        IDailySlackDeliveryRepository? dailyDeliveries = null,
        ISlackClientFactory? slackClientFactory = null,
        IUserSettingsStore? settings = null)
    {
        this.planProvider = planProvider;
        this.deliveryService = deliveryService;
        this.attempts = attempts;
        this.dailyDeliveries = dailyDeliveries;
        this.slackClientFactory = slackClientFactory;
        this.settings = settings;
        DryRunCommand = new RelayCommand(_ => RunDryRun());
        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync());
        OpenTaskConfirmationCommand = new RelayCommand(value =>
        {
            if (value is PlannedWorkItem item)
                OpenTaskConfirmation(item.Id);
        });
        ConfirmTaskCommand = new RelayCommand(_ => _ = ConfirmTaskAsync(), () => CanConfirmTask);
        CancelTaskConfirmationCommand = new RelayCommand(_ => CancelTaskConfirmation(), () => IsTaskConfirmationVisible);
        ComposeSlackPreviewCommand = new RelayCommand(_ => _ = ComposeSlackPreviewAsync());
        ConfirmSlackCommand = new RelayCommand(_ => _ = ConfirmSlackAsync(), () => CanConfirmSlack);
        CancelSlackConfirmationCommand = new RelayCommand(_ => CancelSlackConfirmation(), () => IsSlackConfirmationVisible);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RelayCommand DryRunCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand OpenTaskConfirmationCommand { get; }
    public RelayCommand ConfirmTaskCommand { get; }
    public RelayCommand CancelTaskConfirmationCommand { get; }
    public RelayCommand ComposeSlackPreviewCommand { get; }
    public RelayCommand ConfirmSlackCommand { get; }
    public RelayCommand CancelSlackConfirmationCommand { get; }
    public ObservableCollection<PlannedWorkItem> Items { get; } = [];
    public ObservableCollection<string> DryRunBlockers { get; } = [];
    public ObservableCollection<string> SlackBlockers { get; } = [];
    public string DryRunSummary { get => dryRunSummary; private set => SetField(ref dryRunSummary, value); }
    public PlannedWorkItem? SelectedTask { get => selectedTask; private set => SetField(ref selectedTask, value); }
    public DeliveryAttempt? LastTaskAttempt { get => lastTaskAttempt; private set => SetField(ref lastTaskAttempt, value); }
    public SlackDailyUpdate? SlackPreview { get => slackPreview; private set => SetField(ref slackPreview, value); }
    public string? TaskDeliveryError { get => taskDeliveryError; private set => SetField(ref taskDeliveryError, value); }
    public string? SlackDeliveryError { get => slackDeliveryError; private set => SetField(ref slackDeliveryError, value); }
    public bool IsTaskDeliveryInFlight
    {
        get => isTaskDeliveryInFlight;
        private set
        {
            if (isTaskDeliveryInFlight == value) return;
            SetField(ref isTaskDeliveryInFlight, value);
            ConfirmTaskCommand.NotifyCanExecuteChanged();
        }
    }
    public bool CanConfirmTask => IsTaskConfirmationVisible && !IsTaskDeliveryInFlight;
    public string TaskDeliveryStatus => LastTaskAttempt?.Status.ToString() ?? "Not delivered";
    public bool IsTaskConfirmationVisible
    {
        get => isTaskConfirmationVisible;
        private set
        {
            if (isTaskConfirmationVisible == value) return;
            SetField(ref isTaskConfirmationVisible, value);
            ConfirmTaskCommand.NotifyCanExecuteChanged();
            CancelTaskConfirmationCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanConfirmTask));
        }
    }
    public bool IsSlackConfirmationVisible
    {
        get => isSlackConfirmationVisible;
        private set
        {
            if (isSlackConfirmationVisible == value) return;
            SetField(ref isSlackConfirmationVisible, value);
            CancelSlackConfirmationCommand.NotifyCanExecuteChanged();
        }
    }
    public bool CanConfirmSlack
    {
        get => canConfirmSlack;
        private set
        {
            if (canConfirmSlack == value) return;
            SetField(ref canConfirmSlack, value);
            ConfirmSlackCommand.NotifyCanExecuteChanged();
        }
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Items.Clear();
        var plan = planProvider?.GetSnapshot();
        if (plan is not null)
            foreach (var item in plan.Items)
                Items.Add(item);
        return Task.CompletedTask;
    }

    public void OpenTaskConfirmation(Guid itemId)
    {
        var item = planProvider?.GetSnapshot().Items.SingleOrDefault(value => value.Id == itemId);
        if (item is null) return;
        SelectedTask = item;
        if (LastTaskAttempt?.PlannedWorkItemId != item.Id)
            LastTaskAttempt = null;
        TaskDeliveryError = null;
        IsTaskConfirmationVisible = true;
    }

    public void CancelTaskConfirmation()
    {
        IsTaskConfirmationVisible = false;
        SelectedTask = null;
    }

    public async Task ConfirmTaskAsync(CancellationToken cancellationToken = default)
    {
        if (!CanConfirmTask || SelectedTask is not { } item || deliveryService is null) return;

        IsTaskDeliveryInFlight = true;
        CancelTaskConfirmation();
        try
        {
            LastTaskAttempt = await deliveryService.DeliverConfirmedAsync(item, cancellationToken);
            TaskDeliveryError = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TaskDeliveryError = "Task delivery was cancelled.";
        }
        catch
        {
            TaskDeliveryError = "Task delivery could not be completed.";
        }
        finally
        {
            IsTaskDeliveryInFlight = false;
        }
    }

    public async Task ComposeSlackPreviewAsync(CancellationToken cancellationToken = default)
    {
        SlackBlockers.Clear();
        SlackPreview = null;
        SlackDeliveryError = null;
        CanConfirmSlack = false;
        IsSlackConfirmationVisible = false;
        var plan = planProvider?.GetSnapshot();
        if (plan is null || attempts is null || dailyDeliveries is null || slackClientFactory is null)
        {
            SlackBlockers.Add("Daily Slack delivery is unavailable.");
            return;
        }

        try
        {
            if (!await slackClientFactory.IsConfiguredAsync(cancellationToken))
            {
                SlackBlockers.Add("Slack is not configured.");
                return;
            }

            if (await dailyDeliveries.GetAsync(plan.Date, cancellationToken) is not null)
            {
                SlackBlockers.Add("A daily Slack delivery already exists and cannot be sent again.");
                return;
            }

            var completed = new List<SlackDailyCompletedItem>();
            foreach (var item in plan.Items)
            {
                var attempt = await attempts.GetAsync(item.Id, cancellationToken);
                if (attempt is { Status: DeliveryAttemptStatus.Succeeded, TempoWorklogId: not null })
                    completed.Add(new SlackDailyCompletedItem(item.TogglProject, item.JiraIssueKey, item.Comment, item.Status));
                else
                    SlackBlockers.Add("A pending or unsuccessful task delivery is excluded from Slack.");
            }

            var preferences = settings?.Load() ?? new UserSettings();
            SlackPreview = new SlackDailyUpdateComposer().Compose(plan.Date, completed,
                new SlackDailyUpdateOptions(preferences.SlackTitle, preferences.SlackTaskHeading, preferences.SlackExtraLines));
            if (SlackPreview is null)
            {
                SlackBlockers.Add("No Tempo-succeeded tasks are available for Slack.");
                return;
            }

            CanConfirmSlack = true;
            IsSlackConfirmationVisible = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SlackBlockers.Add("Daily Slack preview was cancelled.");
        }
        catch
        {
            SlackBlockers.Add("Daily Slack preview is unavailable.");
        }
    }

    public void CancelSlackConfirmation()
    {
        IsSlackConfirmationVisible = false;
        CanConfirmSlack = false;
    }

    public async Task ConfirmSlackAsync(CancellationToken cancellationToken = default)
    {
        if (!CanConfirmSlack || SlackPreview is null || dailyDeliveries is null || slackClientFactory is null) return;

        CanConfirmSlack = false;
        IsSlackConfirmationVisible = false;
        try
        {
            if (!await slackClientFactory.IsConfiguredAsync(cancellationToken))
            {
                SlackDeliveryError = "Slack is not configured.";
                return;
            }

            ISlackClient client;
            try
            {
                client = await slackClientFactory.CreateAsync(cancellationToken);
            }
            catch
            {
                SlackDeliveryError = "Slack is not configured.";
                return;
            }

            using (client)
            {
            if (!await dailyDeliveries.TryClaimAsync(SlackPreview.Date, SlackPreview.ContentFingerprint, cancellationToken))
            {
                SlackDeliveryError = "A daily Slack delivery already exists and cannot be sent again.";
                return;
            }

            await client.PostAsync(SlackPreview, cancellationToken);
            await dailyDeliveries.SaveAsync(new DailySlackDelivery(SlackPreview.Date, SlackPreview.ContentFingerprint, DailySlackDeliveryState.Sent, null), CancellationToken.None);
            SlackDeliveryError = null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await MarkSlackReconciliationRequiredAsync(DailySlackFailureCode.Cancelled);
        }
        catch (SlackApiException exception)
        {
            await MarkSlackReconciliationRequiredAsync(exception.FailureCode switch
            {
                SlackFailureCode.UnsuccessfulResponse => DailySlackFailureCode.UnsuccessfulResponse,
                SlackFailureCode.InvalidResponse => DailySlackFailureCode.InvalidResponse,
                SlackFailureCode.Cancelled => DailySlackFailureCode.Cancelled,
                _ => DailySlackFailureCode.Transport
            });
        }
        catch
        {
            await MarkSlackReconciliationRequiredAsync(DailySlackFailureCode.PersistenceFailed);
        }
    }

    private async Task MarkSlackReconciliationRequiredAsync(DailySlackFailureCode failureCode)
    {
        try
        {
            await dailyDeliveries!.SaveAsync(new DailySlackDelivery(SlackPreview!.Date, SlackPreview.ContentFingerprint,
                DailySlackDeliveryState.ReconciliationRequired, failureCode), CancellationToken.None);
        }
        catch
        {
        }
        SlackDeliveryError = "Daily Slack delivery requires reconciliation.";
    }

    private void RunDryRun()
    {
        DryRunBlockers.Clear();
        var plan = planProvider?.GetSnapshot();
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
        DryRunSummary = $"{plan.Items.Count} planned item(s), {duration.TotalMinutes:0} planned minute(s). Dry Run does not deliver tasks or Slack.";
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (propertyName == nameof(LastTaskAttempt))
            OnPropertyChanged(nameof(TaskDeliveryStatus));
    }

    private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
