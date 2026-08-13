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
    public async Task Slack_preview_excludes_non_tempo_succeeded_tasks_and_shows_a_safe_blocker()
    {
        var date = new DateOnly(2026, 8, 13);
        var succeeded = PlannedWorkItem.Create(date, "Done", "CGM-1", "Completed", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT");
        var failed = PlannedWorkItem.Create(date, "Failed", "CGM-2", "Not completed", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT");
        var review = CreateReview(
            DailyPlan.Create(date, [succeeded, failed]),
            attempts: new AttemptRepository(Succeeded(succeeded), new DeliveryAttempt(failed.Id, null, null, DeliveryAttemptStatus.Failed, DeliveryFailureCode.TempoFailed, SlackDeliveryState.NotSupported)));

        await review.ComposeSlackPreviewAsync();

        Assert.Contains("GDK | CGM-1 Completed | *In Progress*", review.SlackPreview!.Text);
        Assert.DoesNotContain("CGM-2", review.SlackPreview.Text);
        Assert.Single(review.SlackBlockers);
    }

    [Fact]
    public async Task Existing_sent_or_reconciliation_daily_record_keeps_slack_send_unavailable()
    {
        var date = new DateOnly(2026, 8, 13);
        var item = PlannedWorkItem.Create(date, "Work", "CGM-1", "Completed", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT");
        var slack = new RecordingSlackClientFactory();
        var review = CreateReview(
            DailyPlan.Create(date, [item]),
            attempts: new AttemptRepository(Succeeded(item)),
            slackFactory: slack,
            dailyDeliveries: new DailyDeliveryRepository(new DailySlackDelivery(date, new string('A', 64), DailySlackDeliveryState.Sent, null)));

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
        IDailySlackDeliveryRepository? dailyDeliveries = null) =>
        new(
            new FixedPlanSnapshotProvider(plan),
            delivery ?? new RecordingConfirmedDeliveryService(),
            attempts ?? new AttemptRepository(),
            dailyDeliveries ?? new DailyDeliveryRepository(),
            slackFactory ?? new RecordingSlackClientFactory());

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
        public Task<DailySlackDelivery?> GetAsync(DateOnly date, CancellationToken cancellationToken = default) => Task.FromResult(delivery);
        public Task<bool> TryClaimAsync(DateOnly date, string contentFingerprint, CancellationToken cancellationToken = default)
        {
            if (delivery is not null) return Task.FromResult(false);
            delivery = new DailySlackDelivery(date, contentFingerprint, DailySlackDeliveryState.InProgress, null);
            return Task.FromResult(true);
        }
        public Task SaveAsync(DailySlackDelivery value, CancellationToken cancellationToken = default) { delivery = value; return Task.CompletedTask; }
    }

    private sealed class RecordingSlackClientFactory : ISlackClientFactory
    {
        public int CreateCalls { get; private set; }
        public RecordingSlackClient Client { get; } = new();
        public Task<ISlackClient> CreateAsync(CancellationToken cancellationToken = default) { CreateCalls++; return Task.FromResult<ISlackClient>(Client); }
    }

    private sealed class RecordingSlackClient : ISlackClient
    {
        public List<SlackDailyUpdate> PostedUpdates { get; } = [];
        public Task PostAsync(SlackDailyUpdate update, CancellationToken cancellationToken = default) { PostedUpdates.Add(update); return Task.CompletedTask; }
        public void Dispose() { }
    }

}
