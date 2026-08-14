using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Desktop.ViewModels;
using GDK.TimeSync.Jira;
using GDK.TimeSync.Tempo;
using GDK.TimeSync.Toggl;

namespace GDK.TimeSync.Tests;

public sealed class LiveValidationViewModelTests
{
    [Fact]
    public async Task Construction_refresh_selection_and_opening_toggl_confirmation_are_read_only()
    {
        var item = CreateItem();
        var safety = new SafetyProbe();
        var viewModel = CreateViewModel(item, safety);

        await viewModel.RefreshAsync();
        viewModel.SelectItem(item.Id);
        viewModel.OpenTogglConfirmation();

        Assert.True(viewModel.IsTogglConfirmationVisible);
        Assert.Equal(item, viewModel.SelectedItem);
        Assert.Equal(0, safety.CredentialReads + safety.FactoryCalls + safety.Writes + safety.TogglCalls + safety.TempoCalls + safety.JiraCalls + safety.SlackPosts);
    }

    [Fact]
    public async Task Review_navigation_and_selection_do_not_touch_live_factories_settings_or_attempt_storage()
    {
        var item = CreateItem();
        var factory = new NoAccessIntegrationClientFactory();
        var settings = new TrackingSettingsStore();
        var attempts = new TrackingAttemptRepository();
        var review = new ReviewViewModel(
            new FixedPlanSnapshotProvider(DailyPlan.Create(item.Day, [item])),
            diagnosticsService: new SafetyProbe(),
            validationService: new LiveIntegrationValidationService(factory, settings, attempts));

        await review.RefreshAsync();
        review.LiveValidation.SelectItem(item.Id);
        review.LiveValidation.OpenTogglConfirmation();
        review.LiveValidation.CancelTogglConfirmation();

        Assert.Equal(0, factory.Calls + settings.LoadCalls + settings.SaveCalls + attempts.Calls);
    }

    [Fact]
    public async Task Confirming_toggl_requires_visible_confirmation_and_selected_item_then_runs_only_toggl()
    {
        var item = CreateItem();
        var safety = new SafetyProbe();
        var viewModel = CreateViewModel(item, safety);

        await viewModel.ConfirmTogglAsync();
        await viewModel.RefreshAsync();
        viewModel.SelectItem(item.Id);
        await viewModel.ConfirmTogglAsync();
        viewModel.OpenTogglConfirmation();
        await viewModel.ConfirmTogglAsync();

        Assert.Equal(1, safety.TogglCalls);
        Assert.Equal(0, safety.JiraCalls + safety.TempoCalls + safety.SlackPosts);
        Assert.False(viewModel.IsTogglConfirmationVisible);
        Assert.Equal("Toggl entry created.", viewModel.StepStatus);
    }

    [Fact]
    public async Task Selection_cannot_change_while_a_confirmed_toggl_action_is_in_flight()
    {
        var first = CreateItem();
        var second = CreateItem() with { Id = Guid.NewGuid(), JiraIssueKey = "GDK-43" };
        var safety = new SafetyProbe { PendingToggl = new TaskCompletionSource<LiveValidationResult>() };
        var viewModel = new LiveValidationViewModel(new FixedPlanSnapshotProvider(DailyPlan.Create(first.Day, [first, second])), safety, safety);
        await viewModel.RefreshAsync();
        viewModel.SelectItem(first.Id);
        viewModel.OpenTogglConfirmation();

        var confirmation = viewModel.ConfirmTogglAsync();
        viewModel.SelectItem(second.Id);

        Assert.Equal(first, viewModel.SelectedItem);
        safety.PendingToggl.SetResult(Result(LiveValidationStep.Toggl, first, DeliveryAttemptStatus.InProgress, togglId: 44));
        await confirmation;
    }

    [Fact]
    public async Task Explicit_jira_validation_runs_only_the_read_only_jira_operation()
    {
        var item = CreateItem();
        var safety = new SafetyProbe();
        var viewModel = CreateViewModel(item, safety);
        await viewModel.RefreshAsync();
        viewModel.SelectItem(item.Id);

        await viewModel.ValidateJiraAsync();

        Assert.Equal(1, safety.JiraCalls);
        Assert.Equal(0, safety.TogglCalls + safety.TempoCalls + safety.SlackPosts);
        Assert.Equal("Jira issue validated.", viewModel.StepStatus);
    }

    [Fact]
    public async Task Confirming_tempo_updates_readback_status_without_sending_slack()
    {
        var item = CreateItem();
        var safety = new SafetyProbe { TempoResult = Result(LiveValidationStep.Tempo, item, DeliveryAttemptStatus.Succeeded, tempoId: 55) };
        var viewModel = CreateViewModel(item, safety);
        await viewModel.RefreshAsync();
        viewModel.SelectItem(item.Id);
        viewModel.OpenTempoConfirmation();

        await viewModel.ConfirmTempoAsync();

        Assert.Equal(1, safety.TempoCalls);
        Assert.Equal(0, safety.TogglCalls + safety.JiraCalls + safety.SlackPosts);
        Assert.Equal("Tempo worklog verified.", viewModel.StepStatus);
        Assert.Null(viewModel.RecoveryMessage);
    }

    [Fact]
    public async Task Cancelling_confirmation_hides_it_without_any_service_call()
    {
        var item = CreateItem();
        var safety = new SafetyProbe();
        var viewModel = CreateViewModel(item, safety);
        await viewModel.RefreshAsync();
        viewModel.SelectItem(item.Id);

        viewModel.OpenTogglConfirmation();
        viewModel.CancelTogglConfirmation();
        viewModel.OpenTempoConfirmation();
        viewModel.CancelTempoConfirmation();

        Assert.False(viewModel.IsTogglConfirmationVisible);
        Assert.False(viewModel.IsTempoConfirmationVisible);
        Assert.Equal(0, safety.DiagnosticsCalls + safety.TogglCalls + safety.JiraCalls + safety.TempoCalls + safety.SlackPosts);
    }

    [Fact]
    public async Task Diagnostics_run_only_after_the_explicit_command()
    {
        var item = CreateItem();
        var safety = new SafetyProbe();
        var viewModel = CreateViewModel(item, safety);
        await viewModel.RefreshAsync();
        viewModel.SelectItem(item.Id);

        Assert.Equal(0, safety.DiagnosticsCalls);
        await viewModel.RunDiagnosticsAsync();

        Assert.Equal(1, safety.DiagnosticsCalls);
        Assert.Equal([new IntegrationDiagnosticResult(IntegrationDiagnosticTarget.Toggl, true, "Available")], viewModel.Diagnostics);
    }

    [Fact]
    public async Task Exception_and_sentinel_details_never_appear_in_presentation_state()
    {
        const string sentinel = "live-validation-secret-sentinel";
        var item = CreateItem();
        var safety = new SafetyProbe
        {
            Diagnostics = [new IntegrationDiagnosticResult(IntegrationDiagnosticTarget.Jira, false, sentinel)],
            TogglException = new InvalidOperationException(sentinel)
        };
        var viewModel = CreateViewModel(item, safety);
        await viewModel.RunDiagnosticsAsync();
        await viewModel.RefreshAsync();
        viewModel.SelectItem(item.Id);
        viewModel.OpenTogglConfirmation();

        await viewModel.ConfirmTogglAsync();

        Assert.DoesNotContain(sentinel, string.Join(" ", viewModel.Diagnostics.Select(result => result.SafeMessage)), StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, viewModel.StepStatus, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, viewModel.RecoveryMessage ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal("Live validation is unavailable.", viewModel.StepStatus);
    }

    private static LiveValidationViewModel CreateViewModel(PlannedWorkItem item, SafetyProbe safety) =>
        new(new FixedPlanSnapshotProvider(DailyPlan.Create(item.Day, [item])), safety, safety);

    private static PlannedWorkItem CreateItem() => PlannedWorkItem.Create(
        new DateOnly(2026, 8, 14), "Validation work", "GDK-42", "Validate integrations", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT", start: new TimeOnly(9, 0), end: new TimeOnly(9, 30));

    private static LiveValidationResult Result(LiveValidationStep step, PlannedWorkItem item, DeliveryAttemptStatus status, long? togglId = null, long? tempoId = null) =>
        new(step, new DeliveryAttempt(item.Id, togglId, tempoId, status, null, SlackDeliveryState.NotSupported), "ignored");

    private sealed class FixedPlanSnapshotProvider(DailyPlan plan) : ILocalPlanSnapshotProvider
    {
        public DailyPlan GetSnapshot() => plan;
    }

    private sealed class SafetyProbe : IIntegrationDiagnosticsService, ILiveIntegrationValidationService
    {
        public int CredentialReads { get; private set; }
        public int FactoryCalls { get; private set; }
        public int Writes { get; private set; }
        public int SlackPosts { get; private set; }
        public int DiagnosticsCalls { get; private set; }
        public int TogglCalls { get; private set; }
        public int JiraCalls { get; private set; }
        public int TempoCalls { get; private set; }
        public IReadOnlyList<IntegrationDiagnosticResult> Diagnostics { get; init; } = [new(IntegrationDiagnosticTarget.Toggl, true, "Available")];
        public LiveValidationResult? TempoResult { get; init; }
        public Exception? TogglException { get; init; }
        public TaskCompletionSource<LiveValidationResult>? PendingToggl { get; init; }

        public Task<IReadOnlyList<IntegrationDiagnosticResult>> RunAsync(CancellationToken cancellationToken = default)
        {
            DiagnosticsCalls++;
            return Task.FromResult(Diagnostics);
        }

        public Task<LiveValidationResult> CreateTogglAsync(PlannedWorkItem item, CancellationToken cancellationToken = default)
        {
            TogglCalls++;
            if (PendingToggl is not null) return PendingToggl.Task;
            return TogglException is null
                ? Task.FromResult(Result(LiveValidationStep.Toggl, item, DeliveryAttemptStatus.InProgress, togglId: 44))
                : Task.FromException<LiveValidationResult>(TogglException);
        }

        public Task<LiveValidationResult> ValidateJiraAsync(PlannedWorkItem item, CancellationToken cancellationToken = default)
        {
            JiraCalls++;
            return Task.FromResult(Result(LiveValidationStep.Jira, item, DeliveryAttemptStatus.Succeeded));
        }

        public Task<LiveValidationResult> CreateAndVerifyTempoAsync(PlannedWorkItem item, CancellationToken cancellationToken = default)
        {
            TempoCalls++;
            return Task.FromResult(TempoResult ?? Result(LiveValidationStep.Tempo, item, DeliveryAttemptStatus.Succeeded, tempoId: 55));
        }
    }

    private sealed class NoAccessIntegrationClientFactory : IIntegrationClientFactory
    {
        public int Calls { get; private set; }
        public Task<ITogglClient> CreateTogglAsync(CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException(); }
        public Task<JiraClient> CreateJiraAsync(CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException(); }
        public Task<TempoClient> CreateTempoAsync(CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException(); }
    }

    private sealed class TrackingSettingsStore : IUserSettingsStore
    {
        public int LoadCalls { get; private set; }
        public int SaveCalls { get; private set; }
        public UserSettings Load() { LoadCalls++; return new UserSettings(); }
        public void Save(UserSettings settings) => SaveCalls++;
    }

    private sealed class TrackingAttemptRepository : IDeliveryAttemptRepository
    {
        public int Calls { get; private set; }
        public Task<DeliveryAttempt?> GetAsync(Guid id, CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException(); }
        public Task<IReadOnlyList<DeliveryAttempt>> ListAsync(CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException(); }
        public Task<DeliveryAttemptClaim> ClaimAsync(Guid id, CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException(); }
        public Task SaveAsync(DeliveryAttempt attempt, CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException(); }
    }
}
