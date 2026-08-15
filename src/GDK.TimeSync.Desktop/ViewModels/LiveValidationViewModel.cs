using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.Services;

namespace GDK.TimeSync.Desktop.ViewModels;

public sealed class LiveValidationViewModel : INotifyPropertyChanged
{
    private readonly ILocalPlanSnapshotProvider? planProvider;
    private readonly IIntegrationDiagnosticsService? diagnosticsService;
    private readonly ILiveIntegrationValidationService? validationService;
    private PlannedWorkItem? selectedItem;
    private string stepStatus = "Select a planned item to validate integrations.";
    private string? recoveryMessage;
    private bool isTogglConfirmationVisible;
    private bool isTempoConfirmationVisible;
    private bool isInFlight;
    private CancellationTokenSource? operationCancellation;
    private DeliveryAttempt? durableAttempt;
    private string tempoWorker = string.Empty;
    private string tempoBaseUrl = string.Empty;
    private string tempoConfigurationCategory = string.Empty;

    public LiveValidationViewModel(
        ILocalPlanSnapshotProvider? planProvider = null,
        IIntegrationDiagnosticsService? diagnosticsService = null,
        ILiveIntegrationValidationService? validationService = null)
    {
        this.planProvider = planProvider;
        this.diagnosticsService = diagnosticsService;
        this.validationService = validationService;
        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync());
        SelectItemCommand = new RelayCommand(value =>
        {
            if (value is PlannedWorkItem item) _ = SelectItemAsync(item.Id);
        }, () => !IsInFlight);
        RunDiagnosticsCommand = new RelayCommand(_ => _ = RunDiagnosticsAsync(), () => !IsInFlight);
        OpenTogglConfirmationCommand = new RelayCommand(_ => OpenTogglConfirmation(), () => CanOpenTogglConfirmation);
        ConfirmTogglCommand = new RelayCommand(_ => _ = ConfirmTogglAsync(), () => CanConfirmToggl);
        CancelTogglConfirmationCommand = new RelayCommand(_ => CancelTogglConfirmation(), () => IsTogglConfirmationVisible && !IsInFlight);
        ValidateJiraCommand = new RelayCommand(_ => _ = ValidateJiraAsync(), () => SelectedItem is not null && !IsInFlight);
        OpenTempoConfirmationCommand = new RelayCommand(_ => OpenTempoConfirmation(), () => CanOpenTempoConfirmation);
        ConfirmTempoCommand = new RelayCommand(_ => _ = ConfirmTempoAsync(), () => CanConfirmTempo);
        CancelTempoConfirmationCommand = new RelayCommand(_ => CancelTempoConfirmation(), () => IsTempoConfirmationVisible && !IsInFlight);
        CancelOperationCommand = new RelayCommand(_ => CancelOperation(), () => IsInFlight && operationCancellation is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RelayCommand RefreshCommand { get; }
    public RelayCommand SelectItemCommand { get; }
    public RelayCommand RunDiagnosticsCommand { get; }
    public RelayCommand OpenTogglConfirmationCommand { get; }
    public RelayCommand ConfirmTogglCommand { get; }
    public RelayCommand CancelTogglConfirmationCommand { get; }
    public RelayCommand ValidateJiraCommand { get; }
    public RelayCommand OpenTempoConfirmationCommand { get; }
    public RelayCommand ConfirmTempoCommand { get; }
    public RelayCommand CancelTempoConfirmationCommand { get; }
    public RelayCommand CancelOperationCommand { get; }
    public ObservableCollection<PlannedWorkItem> Items { get; } = [];
    public ObservableCollection<IntegrationDiagnosticResult> Diagnostics { get; } = [];
    public PlannedWorkItem? SelectedItem { get => selectedItem; private set => SetField(ref selectedItem, value); }
    public string StepStatus { get => stepStatus; private set => SetField(ref stepStatus, value); }
    public string? RecoveryMessage { get => recoveryMessage; private set => SetField(ref recoveryMessage, value); }
    public DeliveryAttempt? DurableAttempt { get => durableAttempt; private set => SetField(ref durableAttempt, value); }
    public string TempoWorker { get => tempoWorker; private set => SetField(ref tempoWorker, value); }
    public string TempoBaseUrl { get => tempoBaseUrl; private set => SetField(ref tempoBaseUrl, value); }
    public string TempoConfigurationCategory { get => tempoConfigurationCategory; private set => SetField(ref tempoConfigurationCategory, value); }
    public bool IsInFlight
    {
        get => isInFlight;
        private set
        {
            if (isInFlight == value) return;
            SetField(ref isInFlight, value);
            NotifyActionCommands();
        }
    }
    public bool IsTogglConfirmationVisible
    {
        get => isTogglConfirmationVisible;
        private set
        {
            if (isTogglConfirmationVisible == value) return;
            SetField(ref isTogglConfirmationVisible, value);
            NotifyActionCommands();
        }
    }
    public bool IsTempoConfirmationVisible
    {
        get => isTempoConfirmationVisible;
        private set
        {
            if (isTempoConfirmationVisible == value) return;
            SetField(ref isTempoConfirmationVisible, value);
            NotifyActionCommands();
        }
    }

    public bool CanOpenTogglConfirmation => SelectedItem is not null && DurableAttempt is null && !IsInFlight;
    public bool CanOpenTempoConfirmation => SelectedItem is not null && DurableAttempt is { Status: DeliveryAttemptStatus.InProgress, TogglEntryId: not null } && !IsInFlight;
    public bool CanConfirmToggl => IsTogglConfirmationVisible && CanOpenTogglConfirmation;
    public bool CanConfirmTempo => IsTempoConfirmationVisible && CanOpenTempoConfirmation;

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsInFlight) return Task.CompletedTask;
        try
        {
            var plan = planProvider?.GetSnapshot();
            LoadItems(plan?.Items ?? []);
        }
        catch
        {
            StepStatus = "Local plan is unavailable.";
        }

        return Task.CompletedTask;
    }

    public void LoadItems(IEnumerable<PlannedWorkItem> items)
    {
        if (IsInFlight) return;
        var selectedId = SelectedItem?.Id;
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
        SelectedItem = selectedId is { } id ? Items.SingleOrDefault(item => item.Id == id) : null;
    }

    private bool SelectItem(Guid itemId)
    {
        if (IsInFlight) return false;
        SelectedItem = Items.SingleOrDefault(item => item.Id == itemId);
        IsTogglConfirmationVisible = false;
        IsTempoConfirmationVisible = false;
        DurableAttempt = null;
        TempoWorker = string.Empty;
        TempoBaseUrl = string.Empty;
        TempoConfigurationCategory = SelectedItem?.TempoCategory ?? string.Empty;
        RecoveryMessage = null;
        if (SelectedItem is not null) StepStatus = "Loading durable validation state.";
        NotifyActionCommands();
        return SelectedItem is not null;
    }

    public async Task SelectItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        if (!SelectItem(itemId) || SelectedItem is not { } item || validationService is null) return;
        IsInFlight = true;
        try
        {
            var preview = await validationService.LoadPreviewAsync(item, cancellationToken);
            if (SelectedItem?.Id != item.Id) return;
            DurableAttempt = preview.Attempt;
            TempoWorker = string.IsNullOrWhiteSpace(preview.TempoWorker) ? "Not configured" : preview.TempoWorker;
            TempoBaseUrl = string.IsNullOrWhiteSpace(preview.TempoBaseUrl) ? "Not configured" : preview.TempoBaseUrl;
            TempoConfigurationCategory = string.IsNullOrWhiteSpace(preview.TempoCategory) ? "Not configured" : preview.TempoCategory;
            ApplyHydratedState();
        }
        catch (OperationCanceledException)
        {
            StepStatus = "Validation state loading was cancelled.";
            RecoveryMessage = "Select the item again to load its durable state.";
        }
        catch
        {
            StepStatus = "Durable validation state is unavailable.";
            RecoveryMessage = "Live actions are blocked until durable state can be loaded safely.";
            DurableAttempt = new DeliveryAttempt(item.Id, null, null, DeliveryAttemptStatus.ReconciliationRequired, DeliveryFailureCode.PersistenceFailed, SlackDeliveryState.NotSupported);
        }
        finally
        {
            IsInFlight = false;
            NotifyActionCommands();
        }
    }

    public void OpenTogglConfirmation()
    {
        if (!CanOpenTogglConfirmation) return;
        IsTempoConfirmationVisible = false;
        IsTogglConfirmationVisible = true;
    }

    public void CancelTogglConfirmation() => IsTogglConfirmationVisible = false;

    public async Task ConfirmTogglAsync(CancellationToken cancellationToken = default)
    {
        if (!CanConfirmToggl || SelectedItem is not { } item || validationService is null) return;
        IsTogglConfirmationVisible = false;
        await RunValidationAsync(LiveValidationStep.Toggl, token => validationService.CreateTogglAsync(item, token), cancellationToken);
    }

    public async Task ValidateJiraAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedItem is not { } item || IsInFlight || validationService is null) return;
        await RunValidationAsync(LiveValidationStep.Jira, token => validationService.ValidateJiraAsync(item, token), cancellationToken);
    }

    public void OpenTempoConfirmation()
    {
        if (!CanOpenTempoConfirmation) return;
        IsTogglConfirmationVisible = false;
        IsTempoConfirmationVisible = true;
    }

    public void CancelTempoConfirmation() => IsTempoConfirmationVisible = false;

    public async Task ConfirmTempoAsync(CancellationToken cancellationToken = default)
    {
        if (!CanConfirmTempo || SelectedItem is not { } item || validationService is null) return;
        IsTempoConfirmationVisible = false;
        await RunValidationAsync(LiveValidationStep.Tempo, token => validationService.CreateAndVerifyTempoAsync(item, token), cancellationToken);
    }

    public void CancelOperation()
    {
        if (!IsInFlight || operationCancellation is null) return;
        StepStatus = "Cancellation requested.";
        operationCancellation.Cancel();
    }

    public async Task RunDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        if (IsInFlight || diagnosticsService is null) return;

        using var ownedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operationCancellation = ownedCancellation;
        IsInFlight = true;
        try
        {
            var results = await diagnosticsService.RunAsync(ownedCancellation.Token);
            Diagnostics.Clear();
            foreach (var result in results)
                Diagnostics.Add(new IntegrationDiagnosticResult(result.Target, result.IsSuccessful, SafeDiagnosticMessage(result)));
            StepStatus = "Diagnostics completed.";
            RecoveryMessage = RecoveryForDurableAttempt(DurableAttempt);
        }
        catch (OperationCanceledException)
        {
            Diagnostics.Clear();
            StepStatus = "Diagnostics cancelled.";
            RecoveryMessage = "Diagnostics were cancelled; no write was attempted.";
        }
        catch
        {
            Diagnostics.Clear();
            StepStatus = "Live validation is unavailable.";
            RecoveryMessage = "Live validation is unavailable.";
        }
        finally
        {
            operationCancellation = null;
            IsInFlight = false;
        }
    }

    private async Task RunValidationAsync(LiveValidationStep step, Func<CancellationToken, Task<LiveValidationResult>> action, CancellationToken cancellationToken)
    {
        using var ownedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operationCancellation = ownedCancellation;
        IsInFlight = true;
        try
        {
            ApplyResult(step, await action(ownedCancellation.Token));
        }
        catch (OperationCanceledException)
        {
            StepStatus = "Validation was cancelled.";
            RecoveryMessage = "Select the item again to review durable state before any further action.";
        }
        catch
        {
            StepStatus = "Live validation is unavailable.";
            RecoveryMessage = "Live validation is unavailable.";
        }
        finally
        {
            operationCancellation = null;
            IsInFlight = false;
        }
    }

    private void ApplyResult(LiveValidationStep step, LiveValidationResult result)
    {
        if (step is LiveValidationStep.Toggl or LiveValidationStep.Tempo)
            DurableAttempt = result.Attempt;

        StepStatus = result.Outcome switch
        {
            LiveValidationOutcome.Created => "Toggl entry created.",
            LiveValidationOutcome.Validated => "Jira issue validated.",
            LiveValidationOutcome.Verified => "Tempo worklog verified.",
            LiveValidationOutcome.Blocked => result.SafeMessage,
            LiveValidationOutcome.ReconciliationRequired => result.SafeMessage,
            LiveValidationOutcome.Cancelled => result.SafeMessage,
            _ => result.SafeMessage
        };
        RecoveryMessage = RecoveryFor(result) ?? (step == LiveValidationStep.Jira ? RecoveryForDurableAttempt(DurableAttempt) : null);
        NotifyActionCommands();
    }

    private void ApplyHydratedState()
    {
        if (DurableAttempt is null)
        {
            StepStatus = "Ready to confirm Toggl creation.";
            RecoveryMessage = null;
            return;
        }

        (StepStatus, RecoveryMessage) = DurableAttempt switch
        {
            { Status: DeliveryAttemptStatus.InProgress, TempoWorklogId: not null } =>
                ("Tempo write is recorded; confirm Tempo to perform readback.", "Do not create another Tempo worklog."),
            { Status: DeliveryAttemptStatus.InProgress, TogglEntryId: not null } =>
                ("Toggl is recorded; validate Jira, then confirm Tempo.", null),
            { Status: DeliveryAttemptStatus.Succeeded } =>
                ("Existing delivery is recorded.", "Existing completed delivery requires manual review; this screen has not read Tempo back."),
            { Status: DeliveryAttemptStatus.ReconciliationRequired } =>
                ("Reconciliation is required.", RecoveryForAttempt(DurableAttempt)),
            { Status: DeliveryAttemptStatus.Cancelled } =>
                ("Previous validation was cancelled.", "Review the durable attempt before deciding any manual recovery; no automatic resend is available."),
            { Status: DeliveryAttemptStatus.Failed } =>
                ("Previous validation failed.", "Review configuration and durable state; this recorded attempt cannot be resent automatically."),
            _ => ("Validation is blocked.", "Review durable state before continuing.")
        };
    }

    private static string? RecoveryFor(LiveValidationResult result) => result.Outcome switch
    {
        LiveValidationOutcome.ReconciliationRequired => RecoveryForAttempt(result.Attempt),
        LiveValidationOutcome.Blocked when result.Attempt.Status == DeliveryAttemptStatus.Succeeded =>
            "Existing completed delivery requires manual review; Tempo was not read back and nothing will be resent.",
        LiveValidationOutcome.Blocked => "Complete the required prior durable step; no write was sent by this blocked action.",
        LiveValidationOutcome.Cancelled => "The operation was cancelled before a confirmed write; select the item again to review durable state.",
        LiveValidationOutcome.Failed => "Review the safe blocker and durable state; no automatic retry is available.",
        _ => null
    };

    private static string? RecoveryForDurableAttempt(DeliveryAttempt? attempt) => attempt switch
    {
        { Status: DeliveryAttemptStatus.Succeeded } =>
            "Existing completed delivery requires manual review; this screen has not read Tempo back.",
        { Status: DeliveryAttemptStatus.ReconciliationRequired } => RecoveryForAttempt(attempt),
        { Status: DeliveryAttemptStatus.Cancelled } =>
            "Review the durable attempt before deciding any manual recovery; no automatic resend is available.",
        { Status: DeliveryAttemptStatus.Failed } =>
            "Review configuration and durable state; this recorded attempt cannot be resent automatically.",
        _ => null
    };

    private static string RecoveryForAttempt(DeliveryAttempt attempt) => attempt switch
    {
        { TempoWorklogId: not null } => "A Tempo worklog may require reconciliation. Verify the recorded Tempo ID manually; do not resend.",
        { TogglEntryId: not null } => "A Toggl entry may require reconciliation. Verify the recorded Toggl ID manually; do not resend.",
        _ => "An external write may have occurred. Reconcile manually before any further action; do not resend."
    };

    private static string SafeDiagnosticMessage(IntegrationDiagnosticResult result) =>
        result.IsSuccessful ? "Available" : result.SafeMessage == "Cancelled" ? "Cancelled" : "Unavailable";

    private void NotifyActionCommands()
    {
        SelectItemCommand.NotifyCanExecuteChanged();
        RunDiagnosticsCommand.NotifyCanExecuteChanged();
        OpenTogglConfirmationCommand.NotifyCanExecuteChanged();
        ConfirmTogglCommand.NotifyCanExecuteChanged();
        CancelTogglConfirmationCommand.NotifyCanExecuteChanged();
        ValidateJiraCommand.NotifyCanExecuteChanged();
        OpenTempoConfirmationCommand.NotifyCanExecuteChanged();
        ConfirmTempoCommand.NotifyCanExecuteChanged();
        CancelTempoConfirmationCommand.NotifyCanExecuteChanged();
        CancelOperationCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanOpenTogglConfirmation));
        OnPropertyChanged(nameof(CanOpenTempoConfirmation));
        OnPropertyChanged(nameof(CanConfirmToggl));
        OnPropertyChanged(nameof(CanConfirmTempo));
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName is nameof(SelectedItem) or nameof(DurableAttempt)) NotifyActionCommands();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
