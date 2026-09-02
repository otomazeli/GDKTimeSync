using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Desktop.ViewModels;
using GDK.TimeSync.Slack;
using System.Xml.Linq;

namespace GDK.TimeSync.Tests;

public sealed class ReviewViewModelTests
{
    [Fact]
    public void Review_view_is_a_grid_with_one_batch_confirmation_and_no_guided_validation()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GDK.TimeSync.Desktop", "Views", "ReviewView.xaml"));
        var markup = File.ReadAllText(path);
        var elements = XDocument.Load(path).Descendants().ToArray();

        Assert.Contains(elements, element => element.Name.LocalName == "DataGrid");
        Assert.Contains("{Binding Tasks}", markup, StringComparison.Ordinal);
        Assert.Contains("PostSelectedCommand", markup, StringComparison.Ordinal);
        Assert.Contains("ConfirmPostSelectedCommand", markup, StringComparison.Ordinal);
        Assert.Contains("CancelBatchCommand", markup, StringComparison.Ordinal);
        Assert.Contains("BatchConfirmationSummary", markup, StringComparison.Ordinal);
        Assert.Contains("FailureText", markup, StringComparison.Ordinal);

        // The guided-validation block moved to Diagnostics; none of its bindings may remain here.
        Assert.DoesNotContain("IsTogglConfirmationVisible", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("LiveValidation", markup, StringComparison.Ordinal);

        // Exactly one confirmation panel, where there used to be five.
        Assert.Single(markup.Split("IsBatchConfirmationVisible").Skip(1));
    }

    [Fact]
    public async Task AFullPostCycleIsRecordedInOrder()
    {
        var log = new RecordingAuditLog();
        var review = CreateReview(items: [Task30Minutes("CGM-1")], auditLog: log);
        await review.RefreshAsync();

        review.PostSelectedCommand.Execute(null);
        await review.ConfirmPostSelectedAsync();

        var review_entries = log.Entries.Where(entry => entry.Category == "Review").Select(entry => entry.Message).ToArray();
        Assert.Contains(review_entries, message => message.StartsWith("Loaded", StringComparison.Ordinal));
        Assert.Contains(review_entries, message => message.StartsWith("Post requested for 1 task(s): CGM-1", StringComparison.Ordinal));
        Assert.Contains(review_entries, message => message.StartsWith("Post confirmed for 1 task(s)", StringComparison.Ordinal));
        Assert.Contains(review_entries, message => message.StartsWith("Post finished: 1 succeeded, 0 failed", StringComparison.Ordinal));
        Assert.True(Array.IndexOf(review_entries, review_entries.First(m => m.StartsWith("Post requested", StringComparison.Ordinal)))
                 < Array.IndexOf(review_entries, review_entries.First(m => m.StartsWith("Post confirmed", StringComparison.Ordinal))));
    }

    [Fact]
    public async Task CancellingTheConfirmationIsRecordedAndDeliversNothing()
    {
        var log = new RecordingAuditLog();
        var review = CreateReview(items: [Task30Minutes("CGM-1")], auditLog: log, delivery: out var delivery);
        await review.RefreshAsync();

        review.PostSelectedCommand.Execute(null);
        review.CancelPostSelectedCommand.Execute(null);

        Assert.Contains(log.Entries, entry => entry.Category == "Review" && entry.Message.StartsWith("Post cancelled before delivery", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Entries, entry => entry.Category == "Delivery");
        Assert.Equal(0, delivery.Calls);
    }

    [Fact]
    public async Task NoAuditEntryCarriesASettingsValueOrSecret()
    {
        var log = new RecordingAuditLog();
        var review = CreateReview(items: [Task30Minutes("CGM-1")], auditLog: log,
            settings: new UserSettings { JiraBaseUrl = "https://jira.example.test", JiraUser = "secret.user@example.test" });
        await review.RefreshAsync();
        review.PostSelectedCommand.Execute(null);
        await review.ConfirmPostSelectedAsync();

        Assert.All(log.Entries, entry =>
        {
            Assert.DoesNotContain("secret.user@example.test", entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("jira.example.test", entry.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task PostSelected_WritesNothingUntilTheSecondConfirmation()
    {
        var review = CreateReview(items: [Task30Minutes("CGM-1")], delivery: out var delivery);
        await review.RefreshAsync();

        review.PostSelectedCommand.Execute(null);

        Assert.True(review.IsBatchConfirmationVisible);
        Assert.Equal(0, delivery.Calls);
        Assert.Contains("1 task", review.BatchConfirmationSummary, StringComparison.Ordinal);

        await review.ConfirmPostSelectedAsync();

        Assert.Equal(1, delivery.Calls);
        Assert.False(review.IsBatchConfirmationVisible);
    }

    [Fact]
    public async Task PostSelected_DeliversOnlyTickedRowsInOrder()
    {
        var first = Task30Minutes("CGM-1");
        var second = Task30Minutes("CGM-2");
        var third = Task30Minutes("CGM-3");
        var review = CreateReview(items: [first, second, third], delivery: out var delivery);
        await review.RefreshAsync();
        review.Tasks.Single(task => task.JiraIssueKey == "CGM-2").IsSelected = false;

        review.PostSelectedCommand.Execute(null);
        await review.ConfirmPostSelectedAsync();

        Assert.Equal([first.Id, third.Id], delivery.DeliveredIds);
    }

    // One bad Jira key must not strand the rest of the day.
    [Fact]
    public async Task PostSelected_ContinuesAfterAFailureAndReportsBothCounts()
    {
        var failing = Task30Minutes("CGM-1");
        var succeeding = Task30Minutes("CGM-2");
        var review = CreateReview(items: [failing, succeeding], delivery: out var delivery);
        delivery.FailFor(failing.Id, DeliveryFailureCode.TempoFailed, "User is invalid");
        await review.RefreshAsync();

        review.PostSelectedCommand.Execute(null);
        await review.ConfirmPostSelectedAsync();

        Assert.Equal(2, delivery.Calls);
        Assert.Equal(DeliveryMark.Failed, review.Tasks.Single(task => task.Id == failing.Id).Tempo);
        Assert.Equal("Tempo: User is invalid", review.Tasks.Single(task => task.Id == failing.Id).FailureText);
        Assert.Equal(DeliveryMark.Delivered, review.Tasks.Single(task => task.Id == succeeding.Id).Tempo);
        Assert.Contains("1 succeeded", review.BatchStatus!, StringComparison.Ordinal);
        Assert.Contains("1 failed", review.BatchStatus!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellingTheConfirmationDeliversNothing()
    {
        var review = CreateReview(items: [Task30Minutes("CGM-1")], delivery: out var delivery);
        await review.RefreshAsync();

        review.PostSelectedCommand.Execute(null);
        review.CancelPostSelectedCommand.Execute(null);

        Assert.False(review.IsBatchConfirmationVisible);
        Assert.Equal(0, delivery.Calls);
    }

    // Cancel stops before the NEXT task; it never interrupts one already in flight.
    [Fact]
    public async Task CancellingMidRunStopsBeforeTheNextTask()
    {
        var first = Task30Minutes("CGM-1");
        var second = Task30Minutes("CGM-2");
        var review = CreateReview(items: [first, second], delivery: out var delivery);
        await review.RefreshAsync();
        delivery.OnDelivered = _ => review.CancelBatchCommand.Execute(null);

        review.PostSelectedCommand.Execute(null);
        await review.ConfirmPostSelectedAsync();

        Assert.Equal(1, delivery.Calls);
        Assert.Equal([first.Id], delivery.DeliveredIds);
        // A cancel landing mid-delivery must not reach the in-flight call: it must have run to
        // completion on a token that was never cancelled, even though the batch's own token was.
        Assert.All(delivery.Tokens, token => Assert.False(token.IsCancellationRequested));
        Assert.Contains("1 not attempted", review.BatchStatus!, StringComparison.Ordinal);
    }

    // A row a delivery call throws for must show the failure, not whatever it showed before.
    [Fact]
    public async Task PostSelected_AThrownDeliveryMarksTheRowFailedInsteadOfLeavingItStale()
    {
        var review = CreateReview(items: [Task30Minutes("CGM-1")], delivery: out var delivery);
        await review.RefreshAsync();
        delivery.OnDelivered = _ => throw new InvalidOperationException("boom");

        review.PostSelectedCommand.Execute(null);
        await review.ConfirmPostSelectedAsync();

        var row = review.Tasks.Single();
        Assert.NotNull(row.FailureText);
        Assert.Contains("1 failed", review.BatchStatus!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostSelectedIsUnavailableWithNothingTicked()
    {
        var review = CreateReview(items: [Task30Minutes("CGM-1")]);
        await review.RefreshAsync();

        review.Tasks[0].IsSelected = false;

        Assert.False(review.PostSelectedCommand.CanExecute(null));
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
        await review.ComposeSlackPreviewAsync();
        review.CancelSlackConfirmation();

        Assert.Empty(delivery.DeliveredIds);
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

        Assert.Equal([item.Id], review.Tasks.Select(value => value.Id));
        Assert.Empty(delivery.DeliveredIds);
    }

    [Fact]
    public async Task RefreshAsync_BuildsOneRowPerTaskWithItsRecordedAttempt()
    {
        var delivered = PlannedWorkItem.Create(new DateOnly(2026, 9, 1), "A", "CGM-1", "Delivered", TimeSpan.FromMinutes(30));
        var pending = PlannedWorkItem.Create(new DateOnly(2026, 9, 1), "B", "CGM-2", "Pending", TimeSpan.FromMinutes(45));
        var review = CreateReview(
            items: [delivered, pending],
            attempts: new AttemptRepository(new DeliveryAttempt(delivered.Id, 101, 201,
                DeliveryAttemptStatus.Succeeded, null, SlackDeliveryState.NotSupported)));

        await review.RefreshAsync();

        Assert.Equal(2, review.Tasks.Count);
        var deliveredRow = review.Tasks.Single(task => task.Id == delivered.Id);
        Assert.Equal(DeliveryMark.Delivered, deliveredRow.Tempo);
        Assert.False(deliveredRow.IsSelected);
        var pendingRow = review.Tasks.Single(task => task.Id == pending.Id);
        Assert.Equal(DeliveryMark.Pending, pendingRow.Tempo);
        Assert.True(pendingRow.IsSelected);
    }

    [Fact]
    public async Task SelectedCountAndDurationFollowTheTicks()
    {
        var first = PlannedWorkItem.Create(new DateOnly(2026, 9, 1), "A", "CGM-1", "One", TimeSpan.FromMinutes(30));
        var second = PlannedWorkItem.Create(new DateOnly(2026, 9, 1), "B", "CGM-2", "Two", TimeSpan.FromMinutes(45));
        var review = CreateReview(items: [first, second]);
        await review.RefreshAsync();

        Assert.Equal(2, review.SelectedCount);
        Assert.Equal(TimeSpan.FromMinutes(75), review.SelectedDuration);

        review.Tasks[0].IsSelected = false;

        Assert.Equal(1, review.SelectedCount);
        Assert.Equal(TimeSpan.FromMinutes(45), review.SelectedDuration);
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
        Assert.Empty(delivery.DeliveredIds);

        await review.ConfirmSlackAsync();

        Assert.Single(slack.Client.PostedUpdates);
        Assert.Empty(delivery.DeliveredIds);
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
        Assert.Equal("Thank you, team.\nCGM-1 Completed | 🔄 🔷", review.SlackPreview.SlackExtraLines);
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

        Assert.Contains("CGM-1 Completed | 🔄 🔷", review.SlackPreview!.SlackExtraLines);
        Assert.Contains("CGM-2 Not completed | 🔄 ⚪", review.SlackPreview.SlackExtraLines);
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
        Assert.Contains("⚪", review.SlackPreview!.SlackExtraLines);
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
        Assert.Equal("Daily update\nCompleted work\nCGM-1 Completed | 🔄 🔷", clipboard.LastText);
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

    [Fact]
    public async Task ComposeSlackPreviewAsync_looks_up_delivery_attempts_in_a_single_batch_call()
    {
        var date = new DateOnly(2026, 8, 13);
        var items = Enumerable.Range(1, 3)
            .Select(number => PlannedWorkItem.Create(date, $"Work {number}", $"CGM-{number}", "Completed", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT"))
            .ToArray();
        var attempts = new AttemptRepository(items.Select(Succeeded).ToArray());
        var review = CreateReview(DailyPlan.Create(date, items), attempts: attempts);

        await review.ComposeSlackPreviewAsync();

        Assert.Equal(0, attempts.GetCalls);
        Assert.Equal(1, attempts.ListCalls);
    }

    private static ReviewViewModel CreateReview(
        DailyPlan plan,
        IConfirmedTaskDeliveryService? delivery = null,
        IDeliveryAttemptRepository? attempts = null,
        ISlackClientFactory? slackFactory = null,
        IDailySlackDeliveryRepository? dailyDeliveries = null,
        IUserSettingsStore? settings = null,
        IClipboardService? clipboard = null,
        IAuditLog? auditLog = null) =>
        new(
            new FixedPlanSnapshotProvider(plan),
            delivery ?? new RecordingConfirmedDeliveryService(),
            attempts ?? new AttemptRepository(),
            dailyDeliveries ?? new DailyDeliveryRepository(),
            slackFactory ?? new RecordingSlackClientFactory(),
            settings ?? new FixedSettingsStore(new UserSettings()),
            clipboard: clipboard,
            auditLog: auditLog);

    private static ReviewViewModel CreateReview(
        IReadOnlyList<PlannedWorkItem> items,
        IConfirmedTaskDeliveryService? delivery = null,
        IDeliveryAttemptRepository? attempts = null,
        ISlackClientFactory? slackFactory = null,
        IDailySlackDeliveryRepository? dailyDeliveries = null,
        IUserSettingsStore? settings = null,
        IClipboardService? clipboard = null,
        IAuditLog? auditLog = null) =>
        CreateReview(
            DailyPlan.Create(items.Count > 0 ? items[0].Day : default, items),
            delivery, attempts, slackFactory, dailyDeliveries, settings, clipboard, auditLog);

    private static ReviewViewModel CreateReview(IReadOnlyList<PlannedWorkItem> items, out RecordingConfirmedDeliveryService delivery, IAuditLog? auditLog = null)
    {
        delivery = new RecordingConfirmedDeliveryService();
        return CreateReview(items, delivery, auditLog: auditLog);
    }

    // Test 3 needs raw presentation values (JiraBaseUrl/JiraUser) it can assert never leak into the
    // audit log; every other test goes through IUserSettingsStore like the app does.
    private static ReviewViewModel CreateReview(IReadOnlyList<PlannedWorkItem> items, IAuditLog? auditLog, UserSettings settings) =>
        CreateReview(items, settings: new FixedSettingsStore(settings), auditLog: auditLog);

    private static PlannedWorkItem Task30Minutes(string jiraIssueKey) =>
        PlannedWorkItem.Create(new DateOnly(2026, 9, 1), jiraIssueKey, jiraIssueKey, $"Work on {jiraIssueKey}",
            TimeSpan.FromMinutes(30), "CGM", "DEVELOPMENT", start: new TimeOnly(9, 0), end: new TimeOnly(9, 30))
            with { PostToToggl = true };

    private static DeliveryAttempt Succeeded(PlannedWorkItem item) => new(item.Id, 101, 201, DeliveryAttemptStatus.Succeeded, null, SlackDeliveryState.NotSupported);

    private sealed class FixedPlanSnapshotProvider(DailyPlan plan) : ILocalPlanSnapshotProvider
    {
        public DailyPlan GetSnapshot() => plan;
    }

    private sealed class RecordingConfirmedDeliveryService : IConfirmedTaskDeliveryService
    {
        private readonly Dictionary<Guid, (DeliveryFailureCode Code, string Message)> failures = [];
        public List<Guid> DeliveredIds { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public int Calls { get; private set; }
        public Action<PlannedWorkItem>? OnDelivered { get; set; }

        public void FailFor(Guid id, DeliveryFailureCode code, string message) => failures[id] = (code, message);

        public Task<DeliveryAttempt> DeliverConfirmedAsync(PlannedWorkItem item, CancellationToken cancellationToken = default)
        {
            Calls++;
            DeliveredIds.Add(item.Id);
            Tokens.Add(cancellationToken);
            var result = failures.TryGetValue(item.Id, out var failure)
                ? new DeliveryAttempt(item.Id, null, null, DeliveryAttemptStatus.Failed, failure.Code, SlackDeliveryState.NotSupported) { FailureDetail = failure.Message }
                : Succeeded(item);
            OnDelivered?.Invoke(item);
            return Task.FromResult(result);
        }
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
        public int GetCalls { get; private set; }
        public int ListCalls { get; private set; }
        public Task<DeliveryAttempt?> GetAsync(Guid id, CancellationToken cancellationToken = default) { GetCalls++; return Task.FromResult(attempts.GetValueOrDefault(id)); }
        public Task<IReadOnlyList<DeliveryAttempt>> ListAsync(CancellationToken cancellationToken = default) { ListCalls++; return Task.FromResult<IReadOnlyList<DeliveryAttempt>>(attempts.Values.ToArray()); }
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

    private sealed class RecordingAuditLog : IAuditLog
    {
        public List<(AuditLevel Level, string Category, string Message)> Entries { get; } = [];
        public void Write(AuditLevel level, string category, string message) => Entries.Add((level, category, message));
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
