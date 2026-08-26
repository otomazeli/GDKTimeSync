using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Desktop.ViewModels;
using GDK.TimeSync.Slack;

namespace GDK.TimeSync.Tests;

public sealed class ReviewViewModelTests
{
    [Fact]
    public async Task Confirmed_task_delivers_only_the_selected_item()
    {
        var date = new DateOnly(2026, 8, 13);
        var first = PlannedWorkItem.Create(date, "First", "CGM-1", "First work", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT");
        var second = PlannedWorkItem.Create(date, "Second", "CGM-2", "Second work", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT");
        var delivery = new RecordingConfirmedDeliveryService();
        var review = CreateReview(DailyPlan.Create(date, [first, second]), delivery);

        review.OpenTaskConfirmation(first.Id);
        Assert.Empty(delivery.DeliveredItemIds);
        await review.ConfirmTaskAsync();

        Assert.Equal([first.Id], delivery.DeliveredItemIds);
        Assert.Equal(DeliveryAttemptStatus.Succeeded, review.LastTaskAttempt!.Status);
        Assert.False(review.IsTaskConfirmationVisible);
    }

    [Fact]
    public async Task Task_not_marked_for_toggl_is_not_confirmation_eligible()
    {
        var date = new DateOnly(2026, 8, 13);
        var item = PlannedWorkItem.Create(date, "Work", "CGM-1", "Completed", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT") with { PostToToggl = false };
        var delivery = new RecordingConfirmedDeliveryService();
        var review = CreateReview(DailyPlan.Create(date, [item]), delivery);

        review.OpenTaskConfirmation(item.Id);
        await review.ConfirmTaskAsync();

        Assert.False(review.IsTaskConfirmationVisible);
        Assert.Empty(delivery.DeliveredItemIds);
        Assert.Equal("Task is not marked for Toggl delivery.", review.TaskDeliveryError);
    }

    [Fact]
    public void Task_with_a_known_toggl_entry_is_confirmation_eligible_even_when_not_marked_for_toggl()
    {
        var date = new DateOnly(2026, 8, 13);
        var item = PlannedWorkItem.Create(date, "Work", "CGM-1", "Completed", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT") with { PostToToggl = false, TogglEntryId = 42 };
        var review = CreateReview(DailyPlan.Create(date, [item]));

        review.OpenTaskConfirmation(item.Id);

        Assert.True(review.IsTaskConfirmationVisible);
        Assert.Null(review.TaskDeliveryError);
    }

    [Fact]
    public async Task Task_confirmation_projects_selected_details_and_the_safe_completed_result()
    {
        var date = new DateOnly(2026, 8, 13);
        var item = PlannedWorkItem.Create(date, "Planning", "CGM-42", "Review design", TimeSpan.FromMinutes(45), "GDK", "SUPPORT", false);
        var review = CreateReview(DailyPlan.Create(date, [item]));

        review.OpenTaskConfirmation(item.Id);

        Assert.Equal("CGM-42", review.SelectedTask!.JiraIssueKey);
        Assert.Equal("Review design", review.SelectedTask.Comment);
        Assert.Equal(TimeSpan.FromMinutes(45), review.SelectedTask.Duration);
        Assert.Equal("GDK", review.SelectedTask.TogglProject);
        Assert.Equal("SUPPORT", review.SelectedTask.TempoCategory);
        Assert.False(review.SelectedTask.IsBillable);
        Assert.Equal("Not delivered", review.TaskDeliveryStatus);

        await review.ConfirmTaskAsync();

        Assert.Equal("Succeeded", review.TaskDeliveryStatus);
        Assert.Equal(DeliveryAttemptStatus.Succeeded, review.LastTaskAttempt!.Status);
    }

    [Fact]
    public async Task Task_confirmation_closes_synchronously_and_invokes_delivery_once_while_in_flight()
    {
        var date = new DateOnly(2026, 8, 13);
        var item = PlannedWorkItem.Create(date, "Work", "CGM-1", "Completed", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT");
        var delivery = new DelayedConfirmedDeliveryService();
        var review = CreateReview(DailyPlan.Create(date, [item]), delivery);
        review.OpenTaskConfirmation(item.Id);

        var first = review.ConfirmTaskAsync();
        var second = review.ConfirmTaskAsync();

        Assert.False(review.IsTaskConfirmationVisible);
        Assert.False(review.CanConfirmTask);
        Assert.Equal(1, delivery.InvocationCount);
        delivery.Complete(Succeeded(item));
        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task No_confirmation_produces_no_task_delivery_or_slack_post()
    {
        var date = new DateOnly(2026, 8, 13);
        var item = PlannedWorkItem.Create(date, "Work", "CGM-1", "Completed", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT");
        var delivery = new RecordingConfirmedDeliveryService();
        var slack = new RecordingSlackClientFactory();
        var review = CreateReview(DailyPlan.Create(date, [item]), delivery, attempts: new AttemptRepository(Succeeded(item)), slackFactory: slack);

        review.DryRunCommand.Execute(null);
        review.OpenTaskConfirmation(item.Id);
        review.CancelTaskConfirmation();
        await review.ComposeSlackPreviewAsync();
        review.CancelSlackConfirmation();

        Assert.Empty(delivery.DeliveredItemIds);
        Assert.Empty(slack.Client.PostedUpdates);
        Assert.Equal(0, slack.CreateCalls);
    }

    [Fact]
    public async Task RefreshAsync_updates_the_visible_plan_without_delivery()
    {
        var date = new DateOnly(2026, 8, 13);
        var item = PlannedWorkItem.Create(date, "Work", "CGM-1", "Completed", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT");
        var delivery = new RecordingConfirmedDeliveryService();
        var review = CreateReview(DailyPlan.Create(date, [item]), delivery);

        await review.RefreshAsync();

        Assert.Equal([item.Id], review.Items.Select(value => value.Id));
        Assert.Empty(delivery.DeliveredItemIds);
    }

    [Fact]
    public async Task RefreshAsync_reflects_the_plan_snapshots_date()
    {
        var date = new DateOnly(2026, 8, 20);
        var item = PlannedWorkItem.Create(date, "Work", "CGM-1", "Completed", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT");
        var review = CreateReview(DailyPlan.Create(date, [item]), new RecordingConfirmedDeliveryService());

        await review.RefreshAsync();

        Assert.Equal(date, review.PlanDate);
    }

    [Fact]
    public async Task Send_slack_requires_a_separate_final_confirmation_and_never_delivers_tasks()
    {
        var date = new DateOnly(2026, 8, 13);
        var item = PlannedWorkItem.Create(date, "Work", "CGM-1", "Completed", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT");
        var delivery = new RecordingConfirmedDeliveryService();
        var slack = new RecordingSlackClientFactory();
        var review = CreateReview(DailyPlan.Create(date, [item]), delivery, attempts: new AttemptRepository(Succeeded(item)), slackFactory: slack);

        await review.ComposeSlackPreviewAsync();
        Assert.True(review.IsSlackConfirmationVisible);
        Assert.Empty(slack.Client.PostedUpdates);
        Assert.Empty(delivery.DeliveredItemIds);

        await review.ConfirmSlackAsync();

        Assert.Single(slack.Client.PostedUpdates);
        Assert.Empty(delivery.DeliveredItemIds);
        Assert.Equal(1, slack.CreateCalls);
    }

    [Fact]
    public async Task Slack_preview_uses_persisted_non_secret_presentation_preferences()
    {
        var date = new DateOnly(2026, 8, 13);
        var item = PlannedWorkItem.Create(date, "Work", "CGM-1", "Completed", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT");
        var review = CreateReview(
            DailyPlan.Create(date, [item]),
            attempts: new AttemptRepository(Succeeded(item)),
            settings: new FixedSettingsStore(new UserSettings
            {
                SlackTitle = "Daily delivery",
                SlackTaskHeading = "Completed work",
                SlackExtraLines = ["Thank you, team."]
            }));

        await review.ComposeSlackPreviewAsync();

        Assert.Equal("Daily delivery", review.SlackPreview!.SlackTitle);
        Assert.Equal("Completed work", review.SlackPreview.SlackTaskHeading);
        Assert.Equal("Thank you, team.\nGDK | CGM-1 Completed | *In Progress*", review.SlackPreview.SlackExtraLines);
    }

    [Fact]
    public async Task Tampered_presentation_settings_are_sanitized_before_they_reach_the_slack_preview()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"GDK.TimeSync.Tests.{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "settings.json");
            const string sentinel = "https://hooks.slack.com/services%25252525252FT000%25252525252FB000%25252525252Fsentinel-webhook";
            File.WriteAllText(path, $$"""{"SlackTitle":"{{sentinel}}"}""");
            var date = new DateOnly(2026, 8, 13);
            var item = PlannedWorkItem.Create(date, "Work", "CGM-1", "Completed", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT");
            var review = CreateReview(DailyPlan.Create(date, [item]), attempts: new AttemptRepository(Succeeded(item)), settings: new UserSettingsService(path));

            await review.ComposeSlackPreviewAsync();

            Assert.DoesNotContain(sentinel, review.SlackPreview!.SlackTitle, StringComparison.Ordinal);
            Assert.Equal("Daily update", review.SlackPreview.SlackTitle);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_slack_configuration_never_claims_or_creates_a_client()
    {
        var date = new DateOnly(2026, 8, 13);
        var item = PlannedWorkItem.Create(date, "Work", "CGM-1", "Completed", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT");
        var slack = new RecordingSlackClientFactory { IsConfigured = false };
        var deliveries = new DailyDeliveryRepository();
        var review = CreateReview(DailyPlan.Create(date, [item]), attempts: new AttemptRepository(Succeeded(item)), slackFactory: slack, dailyDeliveries: deliveries);

        await review.ComposeSlackPreviewAsync();
        await review.ConfirmSlackAsync();

        Assert.False(review.IsSlackConfirmationVisible);
        Assert.False(review.CanConfirmSlack);
        Assert.Equal(0, deliveries.ClaimCalls);
        Assert.Equal(0, slack.CreateCalls);
    }

    [Fact]
    public async Task Invalid_final_slack_configuration_is_validated_before_claim_or_reconciliation()
    {
        var date = new DateOnly(2026, 8, 13);
        var item = PlannedWorkItem.Create(date, "Work", "CGM-1", "Completed", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT");
        var slack = new InvalidSlackFactory();
        var deliveries = new DailyDeliveryRepository();
        var review = CreateReview(DailyPlan.Create(date, [item]), attempts: new AttemptRepository(Succeeded(item)), slackFactory: slack, dailyDeliveries: deliveries);

        await review.ComposeSlackPreviewAsync();
        await review.ConfirmSlackAsync();

        Assert.Equal(1, slack.GetCalls);
        Assert.Equal(0, deliveries.ClaimCalls);
        Assert.Equal(0, deliveries.SaveCalls);
        Assert.Equal("Slack is not configured.", review.SlackDeliveryError);
    }

    [Fact]
    public async Task Slack_preview_reads_no_credential_and_final_confirmation_reads_once()
    {
        var date = new DateOnly(2026, 8, 13);
        var item = PlannedWorkItem.Create(date, "Work", "CGM-1", "Completed", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT");
        var credentials = new CountingCredentials();
        var slack = new CredentialBackedSlackFactory(credentials);
        var review = CreateReview(DailyPlan.Create(date, [item]), attempts: new AttemptRepository(Succeeded(item)), slackFactory: slack);

        await review.ComposeSlackPreviewAsync();

        Assert.Equal(0, credentials.GetCalls);
        Assert.Equal(1, credentials.ExistsCalls);
        await review.ConfirmSlackAsync();
        Assert.Equal(1, credentials.GetCalls);
    }

    [Fact]
    public async Task Slack_preview_includes_non_tempo_succeeded_tasks_marked_as_not_posted_and_shows_a_safe_blocker()
    {
        var date = new DateOnly(2026, 8, 13);
        var succeeded = PlannedWorkItem.Create(date, "Done", "CGM-1", "Completed", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT");
        var failed = PlannedWorkItem.Create(date, "Failed", "CGM-2", "Not completed", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT");
        var review = CreateReview(
            DailyPlan.Create(date, [succeeded, failed]),
            attempts: new AttemptRepository(Succeeded(succeeded), new DeliveryAttempt(failed.Id, null, null, DeliveryAttemptStatus.Failed, DeliveryFailureCode.TempoFailed, SlackDeliveryState.NotSupported)));

        await review.ComposeSlackPreviewAsync();

        Assert.Contains("GDK | CGM-1 Completed | *In Progress*", review.SlackPreview!.SlackExtraLines);
        Assert.Contains("GDK | CGM-2 Not completed | *In Progress* (not posted in Jira)", review.SlackPreview.SlackExtraLines);
        Assert.Single(review.SlackBlockers);
    }

    [Fact]
    public async Task Slack_preview_composes_even_when_no_task_has_been_posted_to_jira_yet()
    {
        var date = new DateOnly(2026, 8, 13);
        var pending = PlannedWorkItem.Create(date, "Work", "CGM-1", "Not yet delivered", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT");
        var review = CreateReview(DailyPlan.Create(date, [pending]));

        await review.ComposeSlackPreviewAsync();

        Assert.NotNull(review.SlackPreview);
        Assert.Contains("(not posted in Jira)", review.SlackPreview!.SlackExtraLines);
        Assert.True(review.CanConfirmSlack);
    }

    [Fact]
    public async Task CopySlackPreviewCommand_CopiesTheComposedMessageAndIsUnavailableBeforeComposing()
    {
        var date = new DateOnly(2026, 8, 13);
        var item = PlannedWorkItem.Create(date, "Work", "CGM-1", "Completed", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT");
        var clipboard = new FakeClipboardService();
        var review = CreateReview(
            DailyPlan.Create(date, [item]),
            attempts: new AttemptRepository(Succeeded(item)),
            settings: new FixedSettingsStore(new UserSettings { SlackTitle = "Daily update", SlackTaskHeading = "Completed work" }),
            clipboard: clipboard);

        Assert.False(review.CopySlackPreviewCommand.CanExecute(null));

        await review.ComposeSlackPreviewAsync();

        Assert.True(review.CopySlackPreviewCommand.CanExecute(null));
        review.CopySlackPreviewCommand.Execute(null);

        Assert.Equal(1, clipboard.SetTextCalls);
        Assert.Equal("Daily update\nCompleted work\nGDK | CGM-1 Completed | *In Progress*", clipboard.LastText);
    }

    [Theory]
    [InlineData(DailySlackDeliveryState.Sent)]
    [InlineData(DailySlackDeliveryState.ReconciliationRequired)]
    public async Task Existing_final_daily_delivery_state_keeps_slack_send_unavailable(DailySlackDeliveryState state)
    {
        var date = new DateOnly(2026, 8, 13);
        var item = PlannedWorkItem.Create(date, "Work", "CGM-1", "Completed", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT");
        var slack = new RecordingSlackClientFactory();
        var review = CreateReview(
            DailyPlan.Create(date, [item]),
            attempts: new AttemptRepository(Succeeded(item)),
            slackFactory: slack,
            dailyDeliveries: new DailyDeliveryRepository(new DailySlackDelivery(date, new string('A', 64), state, null)));

        await review.ComposeSlackPreviewAsync();
        await review.ConfirmSlackAsync();

        Assert.False(review.CanConfirmSlack);
        Assert.Empty(slack.Client.PostedUpdates);
        Assert.Equal(0, slack.CreateCalls);
    }

    private static ReviewViewModel CreateReview(
        DailyPlan plan,
        IConfirmedTaskDeliveryService? delivery = null,
        IDeliveryAttemptRepository? attempts = null,
        ISlackClientFactory? slackFactory = null,
        IDailySlackDeliveryRepository? dailyDeliveries = null,
        IUserSettingsStore? settings = null,
        IClipboardService? clipboard = null) =>
        new(
            new FixedPlanSnapshotProvider(plan),
            delivery ?? new RecordingConfirmedDeliveryService(),
            attempts ?? new AttemptRepository(),
            dailyDeliveries ?? new DailyDeliveryRepository(),
            slackFactory ?? new RecordingSlackClientFactory(),
            settings ?? new FixedSettingsStore(new UserSettings()),
            clipboard: clipboard);

    private static DeliveryAttempt Succeeded(PlannedWorkItem item) => new(item.Id, 101, 201, DeliveryAttemptStatus.Succeeded, null, SlackDeliveryState.NotSupported);

    private sealed class FixedPlanSnapshotProvider(DailyPlan plan) : ILocalPlanSnapshotProvider
    {
        public DailyPlan GetSnapshot() => plan;
    }

    private sealed class RecordingConfirmedDeliveryService : IConfirmedTaskDeliveryService
    {
        public List<Guid> DeliveredItemIds { get; } = [];
        public Task<DeliveryAttempt> DeliverConfirmedAsync(PlannedWorkItem item, CancellationToken cancellationToken = default)
        {
            DeliveredItemIds.Add(item.Id);
            return Task.FromResult(Succeeded(item));
        }
    }

    private sealed class DelayedConfirmedDeliveryService : IConfirmedTaskDeliveryService
    {
        private readonly TaskCompletionSource<DeliveryAttempt> completion = new();
        public int InvocationCount { get; private set; }
        public Task<DeliveryAttempt> DeliverConfirmedAsync(PlannedWorkItem item, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return completion.Task;
        }
        public void Complete(DeliveryAttempt attempt) => completion.SetResult(attempt);
    }

    private sealed class FixedSettingsStore(UserSettings settings) : IUserSettingsStore
    {
        public UserSettings Load() => settings;
        public void Save(UserSettings value) { }
    }

    private sealed class FakeClipboardService : IClipboardService
    {
        public string? LastText { get; private set; }
        public int SetTextCalls { get; private set; }

        public void SetText(string text)
        {
            SetTextCalls++;
            LastText = text;
        }
    }

    private sealed class AttemptRepository(params DeliveryAttempt[] values) : IDeliveryAttemptRepository
    {
        private readonly Dictionary<Guid, DeliveryAttempt> attempts = values.ToDictionary(value => value.PlannedWorkItemId);
        public Task<DeliveryAttempt?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(attempts.GetValueOrDefault(id));
        public Task<IReadOnlyList<DeliveryAttempt>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DeliveryAttempt>>(attempts.Values.ToArray());
        public Task<DeliveryAttemptClaim> ClaimAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveAsync(DeliveryAttempt attempt, CancellationToken cancellationToken = default) { attempts[attempt.PlannedWorkItemId] = attempt; return Task.CompletedTask; }
    }

    private sealed class DailyDeliveryRepository(DailySlackDelivery? current = null) : IDailySlackDeliveryRepository
    {
        private DailySlackDelivery? delivery = current;
        public int ClaimCalls { get; private set; }
        public int SaveCalls { get; private set; }
        public Task<DailySlackDelivery?> GetAsync(DateOnly date, CancellationToken cancellationToken = default) => Task.FromResult(delivery);
        public Task<bool> TryClaimAsync(DateOnly date, string contentFingerprint, CancellationToken cancellationToken = default)
        {
            ClaimCalls++;
            if (delivery is not null) return Task.FromResult(false);
            delivery = new DailySlackDelivery(date, contentFingerprint, DailySlackDeliveryState.InProgress, null);
            return Task.FromResult(true);
        }
        public Task SaveAsync(DailySlackDelivery value, CancellationToken cancellationToken = default) { SaveCalls++; delivery = value; return Task.CompletedTask; }
    }

    private sealed class RecordingSlackClientFactory : ISlackClientFactory
    {
        public int CreateCalls { get; private set; }
        public bool IsConfigured { get; set; } = true;
        public RecordingSlackClient Client { get; } = new();
        public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default) => Task.FromResult(IsConfigured);
        public Task<ISlackClient> CreateAsync(CancellationToken cancellationToken = default) { CreateCalls++; return Task.FromResult<ISlackClient>(Client); }
    }

    private sealed class RecordingSlackClient : ISlackClient
    {
        public List<SlackDailyUpdate> PostedUpdates { get; } = [];
        public Task PostAsync(SlackDailyUpdate update, CancellationToken cancellationToken = default) { PostedUpdates.Add(update); return Task.CompletedTask; }
        public void Dispose() { }
    }

    private sealed class CountingCredentials : ICredentialStore
    {
        public int GetCalls { get; private set; }
        public int ExistsCalls { get; private set; }
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) { GetCalls++; return Task.FromResult<string?>("not-exposed"); }
        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) { ExistsCalls++; return Task.FromResult(true); }
        public Task SaveAsync(string key, string secret, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class CredentialBackedSlackFactory(CountingCredentials credentials) : ISlackClientFactory
    {
        public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default) => credentials.ExistsAsync(CredentialKeys.SlackWebhook, cancellationToken);
        public async Task<ISlackClient> CreateAsync(CancellationToken cancellationToken = default)
        {
            await credentials.GetAsync(CredentialKeys.SlackWebhook, cancellationToken);
            return new RecordingSlackClient();
        }
    }

    private sealed class InvalidSlackFactory : ISlackClientFactory
    {
        public int GetCalls { get; private set; }
        public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<ISlackClient> CreateAsync(CancellationToken cancellationToken = default)
        {
            GetCalls++;
            throw new InvalidOperationException("invalid credential is not exposed");
        }
    }

}
