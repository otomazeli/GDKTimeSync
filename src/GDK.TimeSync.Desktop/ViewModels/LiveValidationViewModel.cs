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
            if (value is PlannedWorkItem item) SelectItem(item.Id);
        }, () => !IsInFlight);
        RunDiagnosticsCommand = new RelayCommand(_ => _ = RunDiagnosticsAsync(), () => !IsInFlight);
        OpenTogglConfirmationCommand = new RelayCommand(_ => OpenTogglConfirmation(), () => SelectedItem is not null && !IsInFlight);
        ConfirmTogglCommand = new RelayCommand(_ => _ = ConfirmTogglAsync(), () => CanConfirmToggl);
        CancelTogglConfirmationCommand = new RelayCommand(_ => CancelTogglConfirmation(), () => IsTogglConfirmationVisible && !IsInFlight);
        ValidateJiraCommand = new RelayCommand(_ => _ = ValidateJiraAsync(), () => SelectedItem is not null && !IsInFlight);
        OpenTempoConfirmationCommand = new RelayCommand(_ => OpenTempoConfirmation(), () => SelectedItem is not null && !IsInFlight);
        ConfirmTempoCommand = new RelayCommand(_ => _ = ConfirmTempoAsync(), () => CanConfirmTempo);
        CancelTempoConfirmationCommand = new RelayCommand(_ => CancelTempoConfirmation(), () => IsTempoConfirmationVisible && !IsInFlight);
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
    public ObservableCollection<PlannedWorkItem> Items { get; } = [];
    public ObservableCollection<IntegrationDiagnosticResult> Diagnostics { get; } = [];
    public PlannedWorkItem? SelectedItem { get => selectedItem; private set => SetField(ref selectedItem, value); }
    public string StepStatus { get => stepStatus; private set => SetField(ref stepStatus, value); }
    public string? RecoveryMessage { get => recoveryMessage; private set => SetField(ref recoveryMessage, value); }
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

    public bool CanConfirmToggl => IsTogglConfirmationVisible && SelectedItem is not null && !IsInFlight;
    public bool CanConfirmTempo => IsTempoConfirmationVisible && SelectedItem is not null && !IsInFlight;

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
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
        var selectedId = SelectedItem?.Id;
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
        SelectedItem = selectedId is { } id ? Items.SingleOrDefault(item => item.Id == id) : null;
    }

    public void SelectItem(Guid itemId)
    {
        if (IsInFlight) return;
        SelectedItem = Items.SingleOrDefault(item => item.Id == itemId);
        IsTogglConfirmationVisible = false;
        IsTempoConfirmationVisible = false;
        RecoveryMessage = null;
        if (SelectedItem is not null) StepStatus = "Select an explicit validation action.";
    }

    public void OpenTogglConfirmation()
    {
        if (SelectedItem is null || IsInFlight) return;
        IsTempoConfirmationVisible = false;
        IsTogglConfirmationVisible = true;
    }

    public void CancelTogglConfirmation() => IsTogglConfirmationVisible = false;

    public async Task ConfirmTogglAsync(CancellationToken cancellationToken = default)
    {
        if (!CanConfirmToggl || SelectedItem is not { } item || validationService is null) return;
        IsTogglConfirmationVisible = false;
        await RunValidationAsync(LiveValidationStep.Toggl, () => validationService.CreateTogglAsync(item, cancellationToken));
    }

    public async Task ValidateJiraAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedItem is not { } item || IsInFlight || validationService is null) return;
        await RunValidationAsync(LiveValidationStep.Jira, () => validationService.ValidateJiraAsync(item, cancellationToken));
    }

    public void OpenTempoConfirmation()
    {
        if (SelectedItem is null || IsInFlight) return;
        IsTogglConfirmationVisible = false;
        IsTempoConfirmationVisible = true;
    }

    public void CancelTempoConfirmation() => IsTempoConfirmationVisible = false;

    public async Task ConfirmTempoAsync(CancellationToken cancellationToken = default)
    {
        if (!CanConfirmTempo || SelectedItem is not { } item || validationService is null) return;
        IsTempoConfirmationVisible = false;
        await RunValidationAsync(LiveValidationStep.Tempo, () => validationService.CreateAndVerifyTempoAsync(item, cancellationToken));
    }

    public async Task RunDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        if (IsInFlight || diagnosticsService is null) return;

        IsInFlight = true;
        try
        {
            var results = await diagnosticsService.RunAsync(cancellationToken);
            Diagnostics.Clear();
            foreach (var result in results)
                Diagnostics.Add(new IntegrationDiagnosticResult(result.Target, result.IsSuccessful, SafeDiagnosticMessage(result)));
            StepStatus = "Diagnostics completed.";
            RecoveryMessage = null;
        }
        catch
        {
            Diagnostics.Clear();
            StepStatus = "Live validation is unavailable.";
            RecoveryMessage = "Live validation is unavailable.";
        }
        finally
        {
            IsInFlight = false;
        }
    }

    private async Task RunValidationAsync(LiveValidationStep step, Func<Task<LiveValidationResult>> action)
    {
        IsInFlight = true;
        try
        {
            ApplyResult(step, await action());
        }
        catch
        {
            StepStatus = "Live validation is unavailable.";
            RecoveryMessage = "Live validation is unavailable.";
        }
        finally
        {
            IsInFlight = false;
        }
    }

    private void ApplyResult(LiveValidationStep step, LiveValidationResult result)
    {
        RecoveryMessage = result.Attempt.Status == DeliveryAttemptStatus.ReconciliationRequired ? "Reconciliation is required." : null;
        StepStatus = result.Attempt.Status switch
        {
            DeliveryAttemptStatus.InProgress when step == LiveValidationStep.Toggl => "Toggl entry created.",
            DeliveryAttemptStatus.Succeeded when step == LiveValidationStep.Jira => "Jira issue validated.",
            DeliveryAttemptStatus.Succeeded when step == LiveValidationStep.Tempo => "Tempo worklog verified.",
            DeliveryAttemptStatus.Succeeded => "Validation completed.",
            DeliveryAttemptStatus.ReconciliationRequired => "Reconciliation is required.",
            DeliveryAttemptStatus.Cancelled => "Validation was cancelled.",
            _ => "Validation failed."
        };
    }

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
        OnPropertyChanged(nameof(CanConfirmToggl));
        OnPropertyChanged(nameof(CanConfirmTempo));
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName == nameof(SelectedItem)) NotifyActionCommands();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
