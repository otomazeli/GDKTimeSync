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
    private readonly IClipboardService? clipboard;
    private readonly IAuditLog? auditLog;
    private string dryRunSummary = "Run Dry Run to validate the current local plan.";
    private SlackDailyUpdate? slackPreview;
    private string? slackDeliveryError;
    private bool isSlackConfirmationVisible;
    private bool canConfirmSlack;
    private DateOnly? planDate;
    private CancellationTokenSource? batchCancellation;
    private bool isBatchConfirmationVisible;
    private bool isBatchInFlight;
    private string? batchStatus;

    public ReviewViewModel(
        ILocalPlanSnapshotProvider? planProvider = null,
        IConfirmedTaskDeliveryService? deliveryService = null,
        IDeliveryAttemptRepository? attempts = null,
        IDailySlackDeliveryRepository? dailyDeliveries = null,
        ISlackClientFactory? slackClientFactory = null,
        IUserSettingsStore? settings = null,
        IIntegrationDiagnosticsService? diagnosticsService = null,
        ILiveIntegrationValidationService? validationService = null,
        IClipboardService? clipboard = null,
        IAuditLog? auditLog = null)
    {
        this.planProvider = planProvider;
        this.deliveryService = deliveryService;
        this.attempts = attempts;
        this.dailyDeliveries = dailyDeliveries;
        this.slackClientFactory = slackClientFactory;
        this.settings = settings;
        this.clipboard = clipboard;
        this.auditLog = auditLog;
        LiveValidation = new LiveValidationViewModel(planProvider, diagnosticsService, validationService);
        DryRunCommand = new RelayCommand(_ => RunDryRun());
        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync());
        PostSelectedCommand = new RelayCommand(_ => OpenBatchConfirmation(), () => SelectedCount > 0 && !IsBatchInFlight);
        ConfirmPostSelectedCommand = new RelayCommand(_ => _ = ConfirmPostSelectedAsync());
        CancelPostSelectedCommand = new RelayCommand(_ => CancelPostSelected(), () => IsBatchConfirmationVisible);
        CancelBatchCommand = new RelayCommand(_ => CancelBatch(), () => IsBatchInFlight);
        ComposeSlackPreviewCommand = new RelayCommand(_ => _ = ComposeSlackPreviewAsync());
        ConfirmSlackCommand = new RelayCommand(_ => _ = ConfirmSlackAsync(), () => CanConfirmSlack);
        CancelSlackConfirmationCommand = new RelayCommand(_ => CancelSlackConfirmation(), () => IsSlackConfirmationVisible);
        CopySlackPreviewCommand = new RelayCommand(_ => CopySlackPreview(), () => SlackPreview is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RelayCommand DryRunCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand PostSelectedCommand { get; }
    public RelayCommand ConfirmPostSelectedCommand { get; }
    public RelayCommand CancelPostSelectedCommand { get; }
    public RelayCommand CancelBatchCommand { get; }
    public RelayCommand ComposeSlackPreviewCommand { get; }
    public RelayCommand ConfirmSlackCommand { get; }
    public RelayCommand CancelSlackConfirmationCommand { get; }
    public RelayCommand CopySlackPreviewCommand { get; }
    public LiveValidationViewModel LiveValidation { get; }
    public ObservableCollection<ReviewTaskViewModel> Tasks { get; } = [];
    public ObservableCollection<string> DryRunBlockers { get; } = [];
    public ObservableCollection<string> SlackBlockers { get; } = [];
    public string DryRunSummary { get => dryRunSummary; private set => SetField(ref dryRunSummary, value); }
    public DateOnly? PlanDate { get => planDate; private set => SetField(ref planDate, value); }
    public SlackDailyUpdate? SlackPreview
    {
        get => slackPreview;
        private set
        {
            if (ReferenceEquals(slackPreview, value)) return;
            SetField(ref slackPreview, value);
            OnPropertyChanged(nameof(SlackPreviewText));
            CopySlackPreviewCommand.NotifyCanExecuteChanged();
        }
    }
    public string SlackPreviewText => SlackPreview is null
        ? ""
        : string.Join("\n", new[] { SlackPreview.SlackTitle, SlackPreview.SlackTaskHeading, SlackPreview.SlackExtraLines }.Where(part => !string.IsNullOrWhiteSpace(part)));
    public string? SlackDeliveryError { get => slackDeliveryError; private set => SetField(ref slackDeliveryError, value); }
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

    public int SelectedCount => Tasks.Count(task => task.IsSelected);
    public TimeSpan SelectedDuration => Tasks.Where(task => task.IsSelected).Aggregate(TimeSpan.Zero, (total, task) => total + task.Duration);
    public string DaySummary => $"{Tasks.Count} task(s) · {SelectedDuration:h\\:mm} selected";

    public bool IsBatchConfirmationVisible
    {
        get => isBatchConfirmationVisible;
        private set
        {
            if (isBatchConfirmationVisible == value) return;
            SetField(ref isBatchConfirmationVisible, value);
            ConfirmPostSelectedCommand.NotifyCanExecuteChanged();
            CancelPostSelectedCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsBatchInFlight
    {
        get => isBatchInFlight;
        private set
        {
            if (isBatchInFlight == value) return;
            SetField(ref isBatchInFlight, value);
            PostSelectedCommand.NotifyCanExecuteChanged();
            CancelBatchCommand.NotifyCanExecuteChanged();
        }
    }

    public string? BatchStatus { get => batchStatus; private set => SetField(ref batchStatus, value); }
    public string BatchConfirmationSummary =>
        $"{SelectedCount} task(s) → Toggl, Jira, Tempo · {SelectedDuration:h\\:mm} total";

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (LiveValidation.IsInFlight) return;

        foreach (var existing in Tasks) existing.PropertyChanged -= OnTaskChanged;
        Tasks.Clear();

        var plan = planProvider?.GetSnapshot();
        PlanDate = plan?.Date;
        if (plan is null) { NotifySelectionChanged(); return; }

        var recorded = new Dictionary<Guid, DeliveryAttempt>();
        if (attempts is not null)
        {
            try
            {
                foreach (var attempt in await attempts.ListAsync(cancellationToken))
                    recorded[attempt.PlannedWorkItemId] = attempt;
            }
            catch
            {
                // A missing delivery history must not stop the day being reviewed; rows simply show
                // as pending, which is what they were before this page knew about attempts at all.
            }
        }

        foreach (var item in plan.Items)
        {
            var row = new ReviewTaskViewModel(item, recorded.GetValueOrDefault(item.Id));
            row.PropertyChanged += OnTaskChanged;
            Tasks.Add(row);
        }

        LiveValidation.LoadItems(Tasks.Select(task => task.Item).ToArray());
        NotifySelectionChanged();
    }

    private void OnTaskChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReviewTaskViewModel.IsSelected)) NotifySelectionChanged();
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedDuration));
        OnPropertyChanged(nameof(DaySummary));
        PostSelectedCommand.NotifyCanExecuteChanged();
    }

    private void OpenBatchConfirmation()
    {
        if (SelectedCount == 0) return;
        BatchStatus = null;
        IsBatchConfirmationVisible = true;
    }

    public void CancelPostSelected() => IsBatchConfirmationVisible = false;

    public async Task ConfirmPostSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (!IsBatchConfirmationVisible || deliveryService is null || IsBatchInFlight) return;

        IsBatchConfirmationVisible = false;
        IsBatchInFlight = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        batchCancellation = cancellation;
        var succeeded = 0;
        var failed = 0;
        var chosen = Tasks.Where(task => task.IsSelected).ToArray();
        try
        {
            foreach (var row in chosen)
            {
                // Checked before each task, never during one: a cancel must not tear a delivery in half.
                if (cancellation.IsCancellationRequested) break;
                try
                {
                    var attempt = await deliveryService.DeliverConfirmedAsync(row.Item, cancellation.Token);
                    row.ApplyAttempt(attempt);
                    if (attempt.Status == DeliveryAttemptStatus.Succeeded) succeeded++; else failed++;
                }
                catch
                {
                    failed++;
                }
            }
            BatchStatus = $"{succeeded} succeeded, {failed} failed.";
        }
        finally
        {
            batchCancellation = null;
            IsBatchInFlight = false;
            NotifySelectionChanged();
        }
    }

    public void CancelBatch() => batchCancellation?.Cancel();

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

            var attemptsByItemId = (await attempts.ListAsync(cancellationToken)).ToDictionary(attempt => attempt.PlannedWorkItemId);
            var completed = new List<SlackDailyCompletedItem>();
            var notPostedCount = 0;
            foreach (var item in plan.Items)
            {
                var postedToJira = attemptsByItemId.GetValueOrDefault(item.Id) is { Status: DeliveryAttemptStatus.Succeeded, TempoWorklogId: not null };
                if (!postedToJira)
                    notPostedCount++;
                completed.Add(new SlackDailyCompletedItem(item.JiraIssueKey, item.Comment, item.Status, postedToJira));
            }

            if (notPostedCount > 0)
                SlackBlockers.Add($"{notPostedCount} task(s) not yet posted to Jira/Tempo are included, marked \"not posted in Jira.\"");

            var preferences = settings?.Load() ?? new UserSettings();
            SlackPreview = new SlackDailyUpdateComposer().Compose(plan.Date, completed,
                new SlackDailyUpdateOptions(preferences.SlackTitle, preferences.SlackTaskHeading, preferences.SlackExtraLines, preferences.JiraUser));
            if (SlackPreview is null)
            {
                SlackBlockers.Add("No tasks are available for Slack.");
                return;
            }

            CanConfirmSlack = true;
            IsSlackConfirmationVisible = true;
            auditLog?.Write(AuditLevel.Info, "Slack", $"Composed {SlackPreviewText.Split('\n').Length} line(s) for {plan.Date}");
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

    private void CopySlackPreview()
    {
        if (SlackPreview is null) return;
        clipboard?.SetText(SlackPreviewText);
    }

    public async Task ConfirmSlackAsync(CancellationToken cancellationToken = default)
    {
        if (!CanConfirmSlack || SlackPreview is null || dailyDeliveries is null || slackClientFactory is null) return;

        await ConfirmSlackCoreAsync(SlackPreview, dailyDeliveries, slackClientFactory, cancellationToken);
        auditLog?.Write(SlackDeliveryError is null ? AuditLevel.Info : AuditLevel.Error, "Slack",
            SlackDeliveryError is null ? "Sent" : $"Send failed: {SlackDeliveryError}");
    }

    private async Task ConfirmSlackCoreAsync(SlackDailyUpdate preview, IDailySlackDeliveryRepository dailyDeliveries, ISlackClientFactory slackClientFactory, CancellationToken cancellationToken)
    {
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
            if (!await dailyDeliveries.TryClaimAsync(preview.Date, preview.ContentFingerprint, cancellationToken))
            {
                SlackDeliveryError = "A daily Slack delivery already exists and cannot be sent again.";
                return;
            }

            await client.PostAsync(preview, cancellationToken);
            await dailyDeliveries.SaveAsync(new DailySlackDelivery(preview.Date, preview.ContentFingerprint, DailySlackDeliveryState.Sent, null), CancellationToken.None);
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
    }

    private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
