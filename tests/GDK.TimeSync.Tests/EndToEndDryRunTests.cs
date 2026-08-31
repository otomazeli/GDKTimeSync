using GDK.TimeSync.Core;
using GDK.TimeSync.Slack;

namespace GDK.TimeSync.Tests;

/// <summary>
/// Exercises the real end-of-day delivery path against fakes for every external system: Toggl,
/// Jira, Tempo (via <see cref="PostAllCoordinator"/>, the same coordinator the desktop app's
/// confirmed-task delivery uses) and Slack (via <see cref="SlackDailyUpdateComposer"/> and
/// <see cref="ISlackClient"/>, the same collaborators <c>ReviewViewModel</c> wires for the
/// separate final "Send Slack update" confirmation). No network, credential store, or UI is
/// involved; this proves the ordered call shape end to end with nothing left unwired.
/// </summary>
public sealed class EndToEndDryRunTests
{
    [Fact]
    public async Task PostAllThenSlack_DeliversTogglJiraTempoThenSlackInOrder()
    {
        var events = new List<string>();
        var toggl = new RecordingTogglClient(events);
        var jira = new RecordingJiraClient(events);
        var tempo = new RecordingTempoClient(events);
        var attempts = new InMemoryDeliveryAttemptRepository();
        var coordinator = new PostAllCoordinator(toggl, jira, tempo, attempts);
        var slackClient = new RecordingSlackClient(events);
        var dailyDeliveries = new InMemoryDailySlackDeliveryRepository();

        var date = new DateOnly(2026, 8, 26);
        var first = PlannedWorkItem.Create(date, "First", "CGM-1", "First work", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT");
        var second = PlannedWorkItem.Create(date, "Second", "CGM-2", "Second work", TimeSpan.FromMinutes(45), "GDK", "SUPPORT");
        var plan = DailyPlan.Create(date, [first, second]);

        var postAllResult = await coordinator.PostAsync(plan);

        Assert.Equal(["toggl", "jira", "tempo", "toggl", "jira", "tempo"], events);
        Assert.All(postAllResult.Attempts, attempt => Assert.Equal(DeliveryAttemptStatus.Succeeded, attempt.Status));

        var completedItems = new List<SlackDailyCompletedItem>();
        foreach (var item in plan.Items)
        {
            var attempt = await attempts.GetAsync(item.Id);
            var postedToJira = attempt is { Status: DeliveryAttemptStatus.Succeeded, TempoWorklogId: not null };
            completedItems.Add(new SlackDailyCompletedItem(item.JiraIssueKey, item.Comment, item.Status, postedToJira));
        }

        var composer = new SlackDailyUpdateComposer();
        var options = new SlackDailyUpdateOptions("Daily update", "Completed tasks", [], "user@example.com");
        var slackUpdate = composer.Compose(date, completedItems, options);

        Assert.NotNull(slackUpdate);
        Assert.DoesNotContain("not posted in Jira", slackUpdate!.SlackExtraLines, StringComparison.Ordinal);

        var claimed = await dailyDeliveries.TryClaimAsync(slackUpdate.Date, slackUpdate.ContentFingerprint);
        Assert.True(claimed);
        await slackClient.PostAsync(slackUpdate);
        await dailyDeliveries.SaveAsync(new DailySlackDelivery(slackUpdate.Date, slackUpdate.ContentFingerprint, DailySlackDeliveryState.Sent, null));

        Assert.Equal(["toggl", "jira", "tempo", "toggl", "jira", "tempo", "slack"], events);
        var posted = Assert.Single(slackClient.PostedUpdates);
        Assert.Same(slackUpdate, posted);
        Assert.Equal(DailySlackDeliveryState.Sent, (await dailyDeliveries.GetAsync(date))!.State);
    }

    [Fact]
    public async Task PostAllThenSlack_MarksUndeliveredTasksAsNotPostedInJiraInTheSlackMessage()
    {
        var attempts = new InMemoryDeliveryAttemptRepository();
        var coordinator = new PostAllCoordinator(
            new RecordingTogglClient([]),
            new RecordingJiraClient([], issueId: null),
            new RecordingTempoClient([]),
            attempts);

        var date = new DateOnly(2026, 8, 26);
        var item = PlannedWorkItem.Create(date, "Work", "CGM-1", "Completed", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT");
        var plan = DailyPlan.Create(date, [item]);

        var result = await coordinator.PostAsync(plan);
        var failed = Assert.Single(result.Attempts);
        Assert.Equal(DeliveryFailureCode.JiraIssueNotFound, failed.FailureCode);

        var attempt = await attempts.GetAsync(item.Id);
        var postedToJira = attempt is { Status: DeliveryAttemptStatus.Succeeded, TempoWorklogId: not null };
        var completedItems = new List<SlackDailyCompletedItem>
        {
            new(item.JiraIssueKey, item.Comment, item.Status, postedToJira)
        };

        var slackUpdate = new SlackDailyUpdateComposer().Compose(date, completedItems, new SlackDailyUpdateOptions("Daily update", "Completed tasks"));

        Assert.NotNull(slackUpdate);
        Assert.Contains("(not posted in Jira)", slackUpdate!.SlackExtraLines, StringComparison.Ordinal);
    }

    private sealed class RecordingTogglClient(List<string> events) : IPlannedItemTogglClient
    {
        public Task<long> CreateAsync(PlannedWorkItem item, CancellationToken cancellationToken = default)
        {
            events.Add("toggl");
            return Task.FromResult(101L);
        }
    }

    private sealed class RecordingJiraClient(List<string> events, string? issueId = "10001") : IPlannedItemJiraClient
    {
        public Task<string?> GetIssueIdAsync(string issueKey, CancellationToken cancellationToken = default)
        {
            events.Add("jira");
            return Task.FromResult(issueId);
        }
    }

    private sealed class RecordingTempoClient(List<string> events) : IPlannedItemTempoClient
    {
        public Task<long> CreateAsync(PlannedWorkItem item, string jiraIssueId, CancellationToken cancellationToken = default)
        {
            events.Add("tempo");
            return Task.FromResult(201L);
        }
    }

    private sealed class RecordingSlackClient(List<string> events) : ISlackClient
    {
        public List<SlackDailyUpdate> PostedUpdates { get; } = [];

        public Task PostAsync(SlackDailyUpdate update, CancellationToken cancellationToken = default)
        {
            events.Add("slack");
            PostedUpdates.Add(update);
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }

    private sealed class InMemoryDeliveryAttemptRepository : IDeliveryAttemptRepository
    {
        private readonly Dictionary<Guid, DeliveryAttempt> attempts = [];

        public Task<DeliveryAttempt?> GetAsync(Guid plannedWorkItemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(attempts.GetValueOrDefault(plannedWorkItemId));

        public Task<IReadOnlyList<DeliveryAttempt>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DeliveryAttempt>>(attempts.Values.ToArray());

        public Task<DeliveryAttemptClaim> ClaimAsync(Guid plannedWorkItemId, CancellationToken cancellationToken = default)
        {
            if (!attempts.TryGetValue(plannedWorkItemId, out var existing))
            {
                var created = new DeliveryAttempt(plannedWorkItemId, null, null, DeliveryAttemptStatus.InProgress, null, SlackDeliveryState.NotSupported);
                attempts[plannedWorkItemId] = created;
                return Task.FromResult(new DeliveryAttemptClaim(created, true));
            }

            return Task.FromResult(new DeliveryAttemptClaim(existing, false));
        }

        public Task SaveAsync(DeliveryAttempt attempt, CancellationToken cancellationToken = default)
        {
            attempts[attempt.PlannedWorkItemId] = attempt;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryDailySlackDeliveryRepository : IDailySlackDeliveryRepository
    {
        private DailySlackDelivery? delivery;

        public Task<DailySlackDelivery?> GetAsync(DateOnly date, CancellationToken cancellationToken = default) => Task.FromResult(delivery);

        public Task<bool> TryClaimAsync(DateOnly date, string contentFingerprint, CancellationToken cancellationToken = default)
        {
            if (delivery is not null) return Task.FromResult(false);
            delivery = new DailySlackDelivery(date, contentFingerprint, DailySlackDeliveryState.InProgress, null);
            return Task.FromResult(true);
        }

        public Task SaveAsync(DailySlackDelivery value, CancellationToken cancellationToken = default)
        {
            delivery = value;
            return Task.CompletedTask;
        }
    }
}
