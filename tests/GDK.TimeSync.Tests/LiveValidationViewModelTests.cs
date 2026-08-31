using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Desktop.ViewModels;
using GDK.TimeSync.Jira;
using GDK.TimeSync.Tempo;
using GDK.TimeSync.Toggl;
using System.Xml.Linq;

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
        await viewModel.SelectItemAsync(item.Id);
        viewModel.OpenTogglConfirmation();

        Assert.True(viewModel.IsTogglConfirmationVisible);
        Assert.Equal(item, viewModel.SelectedItem);
        Assert.Equal(0, safety.CredentialReads + safety.FactoryCalls + safety.Writes + safety.TogglCalls + safety.TempoCalls + safety.JiraCalls + safety.SlackPosts);
    }

    [Fact]
    public async Task Selecting_an_item_hydrates_durable_state_and_safe_tempo_preview_without_credentials()
    {
        var item = CreateItem();
        var attempt = new DeliveryAttempt(item.Id, 44, null, DeliveryAttemptStatus.InProgress, null, SlackDeliveryState.NotSupported);
        var safety = new SafetyProbe
        {
            Preview = new LiveValidationPreview(attempt, "planner@example.test", "https://jira.example.test", "DEVELOPMENT")
        };
        var viewModel = CreateViewModel(item, safety);
        await viewModel.RefreshAsync();

        await viewModel.SelectItemAsync(item.Id);

        Assert.Equal(attempt, viewModel.DurableAttempt);
        Assert.Equal("planner@example.test", viewModel.TempoWorker);
        Assert.Equal("https://jira.example.test", viewModel.TempoBaseUrl);
        Assert.Equal("DEVELOPMENT", viewModel.TempoConfigurationCategory);
        Assert.False(viewModel.CanOpenTogglConfirmation);
        Assert.True(viewModel.CanOpenTempoConfirmation);
        Assert.Equal(0, safety.CredentialReads + safety.FactoryCalls + safety.Writes);
    }

    [Fact]
    public async Task Review_navigation_and_selection_read_only_safe_preview_without_touching_credentials_or_clients()
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
        await review.LiveValidation.SelectItemAsync(item.Id);
        review.LiveValidation.OpenTogglConfirmation();
        review.LiveValidation.CancelTogglConfirmation();

        Assert.Equal(0, factory.Calls + settings.SaveCalls + attempts.WriteCalls);
        Assert.Equal(1, settings.LoadCalls);
        Assert.Equal(1, attempts.ReadCalls);
    }

    [Fact]
    public async Task Confirming_toggl_requires_visible_confirmation_and_selected_item_then_runs_only_toggl()
    {
        var item = CreateItem();
        var safety = new SafetyProbe();
        var viewModel = CreateViewModel(item, safety);

        await viewModel.ConfirmTogglAsync();
        await viewModel.RefreshAsync();
        await viewModel.SelectItemAsync(item.Id);
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
        var snapshot = new MutablePlanSnapshotProvider(DailyPlan.Create(first.Day, [first, second]));
        var viewModel = new LiveValidationViewModel(snapshot, safety, safety);
        await viewModel.RefreshAsync();
        await viewModel.SelectItemAsync(first.Id);
        viewModel.OpenTogglConfirmation();

        var confirmation = viewModel.ConfirmTogglAsync();
        await viewModel.SelectItemAsync(second.Id);

        Assert.Equal(first, viewModel.SelectedItem);
        safety.PendingToggl.SetResult(Result(LiveValidationStep.Toggl, first, DeliveryAttemptStatus.InProgress, togglId: 44));
        await confirmation;
    }

    [Fact]
    public async Task Refresh_cannot_replace_the_selected_item_while_a_confirmed_toggl_action_is_in_flight()
    {
        var first = CreateItem();
        var second = CreateItem() with { Id = Guid.NewGuid(), JiraIssueKey = "GDK-43" };
        var safety = new SafetyProbe { PendingToggl = new TaskCompletionSource<LiveValidationResult>() };
        var snapshot = new MutablePlanSnapshotProvider(DailyPlan.Create(first.Day, [first, second]));
        var viewModel = new LiveValidationViewModel(snapshot, safety, safety);
        await viewModel.RefreshAsync();
        await viewModel.SelectItemAsync(first.Id);
        viewModel.OpenTogglConfirmation();

        var confirmation = viewModel.ConfirmTogglAsync();
        snapshot.Plan = DailyPlan.Create(first.Day, [second]);
        await viewModel.RefreshAsync();

        Assert.Equal(first, viewModel.SelectedItem);
        safety.PendingToggl.SetResult(Result(LiveValidationStep.Toggl, first, DeliveryAttemptStatus.InProgress, togglId: 44));
        await confirmation;
    }

    [Fact]
    public async Task Review_refresh_cannot_replace_the_validation_selection_while_a_confirmed_action_is_in_flight()
    {
        var first = CreateItem();
        var second = CreateItem() with { Id = Guid.NewGuid(), JiraIssueKey = "GDK-43" };
        var safety = new SafetyProbe { PendingToggl = new TaskCompletionSource<LiveValidationResult>() };
        var snapshot = new MutablePlanSnapshotProvider(DailyPlan.Create(first.Day, [first, second]));
        var review = new ReviewViewModel(snapshot, diagnosticsService: safety, validationService: safety);
        await review.RefreshAsync();
        await review.LiveValidation.SelectItemAsync(first.Id);
        review.LiveValidation.OpenTogglConfirmation();

        var confirmation = review.LiveValidation.ConfirmTogglAsync();
        snapshot.Plan = DailyPlan.Create(first.Day, [second]);
        await review.RefreshAsync();

        Assert.Equal(first, review.LiveValidation.SelectedItem);
        safety.PendingToggl.SetResult(Result(LiveValidationStep.Toggl, first, DeliveryAttemptStatus.InProgress, togglId: 44));
        await confirmation;
    }

    [Fact]
    public void Review_view_wraps_its_content_in_a_vertical_scroll_viewer()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GDK.TimeSync.Desktop", "Views", "ReviewView.xaml"));
        var root = XDocument.Load(path).Root!;
        var scrollViewer = Assert.Single(root.Elements(), element => element.Name.LocalName == "ScrollViewer");

        Assert.Equal("Auto", scrollViewer.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Contains(scrollViewer.Elements(), element => element.Name.LocalName == "StackPanel");
    }

    [Fact]
    public void Review_view_shows_which_date_is_currently_being_reviewed()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GDK.TimeSync.Desktop", "Views", "ReviewView.xaml"));
        var bindings = XDocument.Load(path).Descendants().Attributes().Select(attribute => attribute.Value).ToArray();

        Assert.Contains(bindings, value => value.Contains("PlanDate", StringComparison.Ordinal));
    }

    [Fact]
    public void Review_view_confirmation_panels_show_required_safe_metadata_and_visible_operation_cancellation()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GDK.TimeSync.Desktop", "Views", "ReviewView.xaml"));
        var bindings = XDocument.Load(path).Descendants().Attributes().Select(attribute => attribute.Value).ToArray();

        Assert.Contains(bindings, value => value.Contains("IsTogglConfirmationVisible", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains("SelectedItem.TogglProject", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains("SelectedItem.Start", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains("SelectedItem.End", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains("IsTempoConfirmationVisible", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains("TempoWorker", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains("TempoBaseUrl", StringComparison.Ordinal));
        Assert.Contains(bindings, value => value.Contains("CancelOperationCommand", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Explicit_jira_validation_runs_only_the_read_only_jira_operation()
    {
        var item = CreateItem();
        var safety = new SafetyProbe();
        var viewModel = CreateViewModel(item, safety);
        await viewModel.RefreshAsync();
        await viewModel.SelectItemAsync(item.Id);

        await viewModel.ValidateJiraAsync();

        Assert.Equal(1, safety.JiraCalls);
        Assert.Equal(0, safety.TogglCalls + safety.TempoCalls + safety.SlackPosts);
        Assert.Equal("Jira issue validated.", viewModel.StepStatus);
        Assert.Null(viewModel.RecoveryMessage);
    }

    [Fact]
    public async Task Confirming_tempo_updates_readback_status_without_sending_slack()
    {
        var item = CreateItem();
        var togglAttempt = new DeliveryAttempt(item.Id, 44, null, DeliveryAttemptStatus.InProgress, null, SlackDeliveryState.NotSupported);
        var safety = new SafetyProbe
        {
            Preview = new LiveValidationPreview(togglAttempt, "planner", "https://jira.example.test", "DEVELOPMENT"),
            TempoResult = Result(LiveValidationStep.Tempo, item, DeliveryAttemptStatus.Succeeded, LiveValidationOutcome.Verified, tempoId: 55)
        };
        var viewModel = CreateViewModel(item, safety);
        await viewModel.RefreshAsync();
        await viewModel.SelectItemAsync(item.Id);
        viewModel.OpenTempoConfirmation();

        await viewModel.ConfirmTempoAsync();

        Assert.Equal(1, safety.TempoCalls);
        Assert.Equal(0, safety.TogglCalls + safety.JiraCalls + safety.SlackPosts);
        Assert.Equal("Tempo worklog verified.", viewModel.StepStatus);
        Assert.Null(viewModel.RecoveryMessage);
    }

    [Fact]
    public async Task Preexisting_succeeded_delivery_shows_manual_review_blocker_not_tempo_verification()
    {
        var item = CreateItem();
        var attempt = new DeliveryAttempt(item.Id, 44, 55, DeliveryAttemptStatus.Succeeded, null, SlackDeliveryState.NotSupported);
        var safety = new SafetyProbe
        {
            Preview = new LiveValidationPreview(attempt, "planner", "https://jira.example.test", "DEVELOPMENT"),
            TempoResult = new LiveValidationResult(LiveValidationStep.Tempo, attempt, "Existing delivery requires manual review; Tempo was not read back.", LiveValidationOutcome.Blocked)
        };
        var viewModel = CreateViewModel(item, safety);
        await viewModel.RefreshAsync();
        await viewModel.SelectItemAsync(item.Id);

        await viewModel.ConfirmTempoAsync();

        Assert.NotEqual("Tempo worklog verified.", viewModel.StepStatus);
        Assert.Contains("manual review", viewModel.RecoveryMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.CanOpenTempoConfirmation);
        Assert.Equal(0, safety.TempoCalls);
    }

    [Fact]
    public async Task Blocked_tempo_result_wins_over_succeeded_attempt_status()
    {
        var item = CreateItem();
        var inProgress = new DeliveryAttempt(item.Id, 44, null, DeliveryAttemptStatus.InProgress, null, SlackDeliveryState.NotSupported);
        var succeeded = inProgress with { TempoWorklogId = 55, Status = DeliveryAttemptStatus.Succeeded };
        var safety = new SafetyProbe
        {
            Preview = new LiveValidationPreview(inProgress, "planner", "https://jira.example.test", "DEVELOPMENT"),
            TempoResult = new LiveValidationResult(LiveValidationStep.Tempo, succeeded, "Existing delivery requires manual review; Tempo was not read back.", LiveValidationOutcome.Blocked)
        };
        var viewModel = CreateViewModel(item, safety);
        await viewModel.RefreshAsync();
        await viewModel.SelectItemAsync(item.Id);
        viewModel.OpenTempoConfirmation();

        await viewModel.ConfirmTempoAsync();

        Assert.Equal("Existing delivery requires manual review; Tempo was not read back.", viewModel.StepStatus);
        Assert.Contains("manual review", viewModel.RecoveryMessage, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual("Tempo worklog verified.", viewModel.StepStatus);
    }

    [Theory]
    [InlineData(DeliveryAttemptStatus.Cancelled, "cancelled")]
    [InlineData(DeliveryAttemptStatus.Failed, "failed")]
    [InlineData(DeliveryAttemptStatus.ReconciliationRequired, "reconciliation")]
    public async Task Selecting_recorded_non_success_states_shows_useful_recovery(DeliveryAttemptStatus status, string expected)
    {
        var item = CreateItem();
        var failureCode = status == DeliveryAttemptStatus.Cancelled ? DeliveryFailureCode.Cancelled : DeliveryFailureCode.PersistenceFailed;
        var attempt = new DeliveryAttempt(item.Id, 44, null, status, failureCode, SlackDeliveryState.NotSupported);
        var safety = new SafetyProbe { Preview = new LiveValidationPreview(attempt, "planner", "https://jira.example.test", "DEVELOPMENT") };
        var viewModel = CreateViewModel(item, safety);
        await viewModel.RefreshAsync();

        await viewModel.SelectItemAsync(item.Id);

        Assert.Contains(expected, $"{viewModel.StepStatus} {viewModel.RecoveryMessage}", StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.CanOpenTogglConfirmation);
    }

    [Fact]
    public async Task Visible_cancel_operation_cancels_the_owned_token_and_cannot_resend()
    {
        var item = CreateItem();
        var safety = new SafetyProbe { CompleteTogglOnCancellation = true };
        var viewModel = CreateViewModel(item, safety);
        await viewModel.RefreshAsync();
        await viewModel.SelectItemAsync(item.Id);
        viewModel.OpenTogglConfirmation();

        var operation = viewModel.ConfirmTogglAsync();
        Assert.True(viewModel.CancelOperationCommand.CanExecute(null));
        viewModel.CancelOperation();
        await operation;
        viewModel.OpenTogglConfirmation();
        await viewModel.ConfirmTogglAsync();

        Assert.Equal(1, safety.TogglCalls);
        Assert.True(safety.TogglCancellationObserved);
        Assert.Contains("reconciliation", viewModel.RecoveryMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.CanOpenTogglConfirmation);
    }

    [Fact]
    public async Task Cancelling_confirmation_hides_it_without_any_service_call()
    {
        var item = CreateItem();
        var safety = new SafetyProbe();
        var viewModel = CreateViewModel(item, safety);
        await viewModel.RefreshAsync();
        await viewModel.SelectItemAsync(item.Id);

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
        await viewModel.SelectItemAsync(item.Id);

        Assert.Equal(0, safety.DiagnosticsCalls);
        await viewModel.RunDiagnosticsAsync();

        Assert.Equal(1, safety.DiagnosticsCalls);
        Assert.Equal([new IntegrationDiagnosticResult(IntegrationDiagnosticTarget.Toggl, true, "Available")], viewModel.Diagnostics);
        Assert.Null(viewModel.RecoveryMessage);
    }

    [Fact]
    public async Task Successful_diagnostics_preserve_reconciliation_recovery_guidance()
    {
        var item = CreateItem();
        var attempt = new DeliveryAttempt(item.Id, 44, null, DeliveryAttemptStatus.ReconciliationRequired, DeliveryFailureCode.PersistenceFailed, SlackDeliveryState.NotSupported);
        var safety = new SafetyProbe { Preview = new LiveValidationPreview(attempt, "planner", "https://jira.example.test", "DEVELOPMENT") };
        var viewModel = CreateViewModel(item, safety);
        await viewModel.RefreshAsync();
        await viewModel.SelectItemAsync(item.Id);

        await viewModel.RunDiagnosticsAsync();

        Assert.Contains("reconciliation", viewModel.RecoveryMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not resend", viewModel.RecoveryMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.CanOpenTogglConfirmation);
        Assert.False(viewModel.CanOpenTempoConfirmation);
        Assert.Equal(1, safety.DiagnosticsCalls);
        Assert.Equal(0, safety.TogglCalls + safety.JiraCalls + safety.TempoCalls + safety.SlackPosts);
    }

    [Fact]
    public async Task Successful_jira_validation_preserves_terminal_recovery_guidance()
    {
        var item = CreateItem();
        var attempt = new DeliveryAttempt(item.Id, 44, 55, DeliveryAttemptStatus.Succeeded, null, SlackDeliveryState.NotSupported);
        var safety = new SafetyProbe { Preview = new LiveValidationPreview(attempt, "planner", "https://jira.example.test", "DEVELOPMENT") };
        var viewModel = CreateViewModel(item, safety);
        await viewModel.RefreshAsync();
        await viewModel.SelectItemAsync(item.Id);

        await viewModel.ValidateJiraAsync();

        Assert.Contains("manual review", viewModel.RecoveryMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not resend", viewModel.RecoveryMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.CanOpenTogglConfirmation);
        Assert.False(viewModel.CanOpenTempoConfirmation);
        Assert.Equal(1, safety.JiraCalls);
        Assert.Equal(0, safety.TogglCalls + safety.TempoCalls + safety.SlackPosts);
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
        await viewModel.SelectItemAsync(item.Id);
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

    private static LiveValidationResult Result(LiveValidationStep step, PlannedWorkItem item, DeliveryAttemptStatus status, LiveValidationOutcome? outcome = null, long? togglId = null, long? tempoId = null) =>
        new(step, new DeliveryAttempt(item.Id, togglId, tempoId, status, null, SlackDeliveryState.NotSupported), "ignored", outcome ?? (step == LiveValidationStep.Jira ? LiveValidationOutcome.Validated : step == LiveValidationStep.Tempo ? LiveValidationOutcome.Verified : LiveValidationOutcome.Created));

    private sealed class FixedPlanSnapshotProvider(DailyPlan plan) : ILocalPlanSnapshotProvider
    {
        public DailyPlan GetSnapshot() => plan;
    }

    private sealed class MutablePlanSnapshotProvider(DailyPlan plan) : ILocalPlanSnapshotProvider
    {
        public DailyPlan Plan { get; set; } = plan;
        public DailyPlan GetSnapshot() => Plan;
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
        public LiveValidationPreview? Preview { get; init; }
        public Exception? TogglException { get; init; }
        public TaskCompletionSource<LiveValidationResult>? PendingToggl { get; init; }
        public bool CompleteTogglOnCancellation { get; init; }
        public bool TogglCancellationObserved { get; private set; }

        public Task<LiveValidationPreview> LoadPreviewAsync(PlannedWorkItem item, CancellationToken cancellationToken = default) =>
            Task.FromResult(Preview ?? new LiveValidationPreview(null, "planner", "https://jira.example.test", item.TempoCategory));

        public Task<IReadOnlyList<IntegrationDiagnosticResult>> RunAsync(CancellationToken cancellationToken = default)
        {
            DiagnosticsCalls++;
            return Task.FromResult(Diagnostics);
        }

        public Task<LiveValidationResult> CreateTogglAsync(PlannedWorkItem item, CancellationToken cancellationToken = default)
        {
            TogglCalls++;
            if (PendingToggl is not null) return PendingToggl.Task;
            if (CompleteTogglOnCancellation)
            {
                var completion = new TaskCompletionSource<LiveValidationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                cancellationToken.Register(() =>
                {
                    TogglCancellationObserved = true;
                    completion.TrySetResult(new LiveValidationResult(
                        LiveValidationStep.Toggl,
                        new DeliveryAttempt(item.Id, 44, null, DeliveryAttemptStatus.ReconciliationRequired, DeliveryFailureCode.Cancelled, SlackDeliveryState.NotSupported),
                        "Toggl reconciliation is required.",
                        LiveValidationOutcome.ReconciliationRequired));
                });
                return completion.Task;
            }
            return TogglException is null
                ? Task.FromResult(Result(LiveValidationStep.Toggl, item, DeliveryAttemptStatus.InProgress, togglId: 44))
                : Task.FromException<LiveValidationResult>(TogglException);
        }

        public Task<LiveValidationResult> ValidateJiraAsync(PlannedWorkItem item, CancellationToken cancellationToken = default)
        {
            JiraCalls++;
            return Task.FromResult(Result(LiveValidationStep.Jira, item, DeliveryAttemptStatus.Succeeded, LiveValidationOutcome.Validated));
        }

        public Task<LiveValidationResult> CreateAndVerifyTempoAsync(PlannedWorkItem item, CancellationToken cancellationToken = default)
        {
            TempoCalls++;
            return Task.FromResult(TempoResult ?? Result(LiveValidationStep.Tempo, item, DeliveryAttemptStatus.Succeeded, LiveValidationOutcome.Verified, tempoId: 55));
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
        public UserSettings Load() { LoadCalls++; return new UserSettings { JiraBaseUrl = "https://jira.example.test", JiraUser = "planner", TogglWorkspaceId = 77 }; }
        public void Save(UserSettings settings) => SaveCalls++;
    }

    private sealed class TrackingAttemptRepository : IDeliveryAttemptRepository
    {
        public int ReadCalls { get; private set; }
        public int WriteCalls { get; private set; }
        public Task<DeliveryAttempt?> GetAsync(Guid id, CancellationToken cancellationToken = default) { ReadCalls++; return Task.FromResult<DeliveryAttempt?>(null); }
        public Task<IReadOnlyList<DeliveryAttempt>> ListAsync(CancellationToken cancellationToken = default) { ReadCalls++; return Task.FromResult<IReadOnlyList<DeliveryAttempt>>([]); }
        public Task<DeliveryAttemptClaim> ClaimAsync(Guid id, CancellationToken cancellationToken = default) { WriteCalls++; throw new InvalidOperationException(); }
        public Task SaveAsync(DeliveryAttempt attempt, CancellationToken cancellationToken = default) { WriteCalls++; throw new InvalidOperationException(); }
    }
}
