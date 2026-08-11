using GDK.TimeSync.Core;
using GDK.TimeSync.Persistence;

namespace GDK.TimeSync.Tests;

public sealed class PostAllCoordinatorTests
{
    [Fact]
    public async Task PostAsync_CreatesTogglThenResolvesJiraThenCreatesTempo()
    {
        var events = new List<string>();
        var attempts = new InMemoryDeliveryAttemptRepository();
        var coordinator = new PostAllCoordinator(
            new RecordingTogglClient(events),
            new RecordingJiraClient(events),
            new RecordingTempoClient(events),
            attempts);
        var item = CreateItem();

        var result = await coordinator.PostAsync(DailyPlan.Create(item.Day, [item]));

        Assert.Equal(["toggl", "jira", "tempo"], events);
        var attempt = Assert.Single(result.Attempts);
        Assert.Equal(DeliveryAttemptStatus.Succeeded, attempt.Status);
        Assert.Equal(SlackDeliveryState.NotSupported, attempt.SlackState);
        Assert.Null(attempt.FailureCode);
        Assert.Equal(101, attempt.TogglEntryId);
        Assert.Equal(201, attempt.TempoWorklogId);
    }

    [Fact]
    public async Task PostAsync_SkipsAnAlreadySuccessfulItem()
    {
        var item = CreateItem();
        var attempts = new InMemoryDeliveryAttemptRepository();
        await attempts.SaveAsync(new(item.Id, 101, 201, DeliveryAttemptStatus.Succeeded, null, SlackDeliveryState.NotSupported));
        var toggl = new RecordingTogglClient([]);
        var jira = new RecordingJiraClient([]);
        var tempo = new RecordingTempoClient([]);
        var coordinator = new PostAllCoordinator(toggl, jira, tempo, attempts);

        await coordinator.PostAsync(DailyPlan.Create(item.Day, [item]));

        Assert.Equal(0, toggl.CreateCount);
        Assert.Equal(0, jira.LookupCount);
        Assert.Equal(0, tempo.CreateCount);
    }

    [Fact]
    public async Task PostAsync_ResumesAfterTempoFailureWithoutDuplicatingToggl()
    {
        var item = CreateItem();
        var attempts = new InMemoryDeliveryAttemptRepository();
        var toggl = new RecordingTogglClient([]);
        var jira = new RecordingJiraClient([]);
        var tempo = new RecordingTempoClient([]) { FailNextCreate = true };
        var coordinator = new PostAllCoordinator(toggl, jira, tempo, attempts);

        var failed = await coordinator.PostAsync(DailyPlan.Create(item.Day, [item]));
        var recovered = await coordinator.PostAsync(DailyPlan.Create(item.Day, [item]));

        Assert.Equal(DeliveryAttemptStatus.Failed, Assert.Single(failed.Attempts).Status);
        Assert.Equal(DeliveryFailureCode.TempoFailed, Assert.Single(failed.Attempts).FailureCode);
        Assert.Equal(101, Assert.Single(failed.Attempts).TogglEntryId);
        Assert.Equal(1, toggl.CreateCount);
        Assert.Equal(2, jira.LookupCount);
        Assert.Equal(2, tempo.CreateCount);
        Assert.Equal(DeliveryAttemptStatus.Succeeded, Assert.Single(recovered.Attempts).Status);
        Assert.Equal(201, Assert.Single(recovered.Attempts).TempoWorklogId);
    }

    [Fact]
    public async Task PostAsync_RecordsCancellationAndStopsBeforeTheNextItem()
    {
        var first = CreateItem();
        var second = CreateItem();
        using var cancellation = new CancellationTokenSource();
        var attempts = new InMemoryDeliveryAttemptRepository();
        var toggl = new RecordingTogglClient([], () => cancellation.Cancel());
        var coordinator = new PostAllCoordinator(toggl, new RecordingJiraClient([]), new RecordingTempoClient([]), attempts);

        var result = await coordinator.PostAsync(DailyPlan.Create(first.Day, [first, second]), cancellation.Token);

        var attempt = Assert.Single(result.Attempts);
        Assert.Equal(first.Id, attempt.PlannedWorkItemId);
        Assert.Equal(DeliveryAttemptStatus.Cancelled, attempt.Status);
        Assert.Equal(DeliveryFailureCode.Cancelled, attempt.FailureCode);
        Assert.Equal(SlackDeliveryState.NotSupported, attempt.SlackState);
        Assert.Equal(1, toggl.CreateCount);
    }

    [Fact]
    public async Task PostAsync_StoresOnlyASafeFailureCode()
    {
        var item = CreateItem();
        var attempts = new InMemoryDeliveryAttemptRepository();
        var toggl = new RecordingTogglClient([]) { Failure = new InvalidOperationException("Bearer sensitive-token") };
        var coordinator = new PostAllCoordinator(toggl, new RecordingJiraClient([]), new RecordingTempoClient([]), attempts);

        var result = await coordinator.PostAsync(DailyPlan.Create(item.Day, [item]));

        var attempt = Assert.Single(result.Attempts);
        Assert.Equal(DeliveryFailureCode.TogglFailed, attempt.FailureCode);
        Assert.Equal(SlackDeliveryState.NotSupported, attempt.SlackState);
    }

    [Fact]
    public async Task PostAsync_RecordsMissingJiraIssueWithoutCreatingTempo()
    {
        var item = CreateItem();
        var attempts = new InMemoryDeliveryAttemptRepository();
        var tempo = new RecordingTempoClient([]);
        var coordinator = new PostAllCoordinator(
            new RecordingTogglClient([]),
            new RecordingJiraClient([], issueId: null),
            tempo,
            attempts);

        var result = await coordinator.PostAsync(DailyPlan.Create(item.Day, [item]));

        var attempt = Assert.Single(result.Attempts);
        Assert.Equal(DeliveryAttemptStatus.Failed, attempt.Status);
        Assert.Equal(DeliveryFailureCode.JiraIssueNotFound, attempt.FailureCode);
        Assert.Equal(101, attempt.TogglEntryId);
        Assert.Null(attempt.TempoWorklogId);
        Assert.Equal(0, tempo.CreateCount);
        Assert.Equal(SlackDeliveryState.NotSupported, attempt.SlackState);
    }

    private static PlannedWorkItem CreateItem() => PlannedWorkItem.Create(
        new DateOnly(2026, 8, 10),
        name: "Daily work",
        jiraIssueKey: "CGM-42",
        comment: "Daily work",
        duration: TimeSpan.FromMinutes(30));

    private sealed class InMemoryDeliveryAttemptRepository : IDeliveryAttemptRepository
    {
        private readonly Dictionary<Guid, DeliveryAttempt> attempts = [];

        public Task<DeliveryAttempt?> GetAsync(Guid plannedWorkItemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(attempts.GetValueOrDefault(plannedWorkItemId));

        public Task SaveAsync(DeliveryAttempt attempt, CancellationToken cancellationToken = default)
        {
            attempts[attempt.PlannedWorkItemId] = attempt;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingTogglClient(List<string> events, Action? onCreate = null) : IPlannedItemTogglClient
    {
        public int CreateCount { get; private set; }
        public Exception? Failure { get; init; }

        public Task<long> CreateAsync(PlannedWorkItem item, CancellationToken cancellationToken = default)
        {
            CreateCount++;
            events.Add("toggl");
            onCreate?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null)
                throw Failure;
            return Task.FromResult(101L);
        }
    }

    private sealed class RecordingJiraClient(List<string> events, string? issueId = "10001") : IPlannedItemJiraClient
    {
        public int LookupCount { get; private set; }

        public Task<string?> GetIssueIdAsync(string issueKey, CancellationToken cancellationToken = default)
        {
            LookupCount++;
            events.Add("jira");
            return Task.FromResult(issueId);
        }
    }

    private sealed class RecordingTempoClient(List<string> events) : IPlannedItemTempoClient
    {
        public int CreateCount { get; private set; }
        public bool FailNextCreate { get; set; }

        public Task<long> CreateAsync(PlannedWorkItem item, string jiraIssueId, CancellationToken cancellationToken = default)
        {
            CreateCount++;
            events.Add("tempo");
            if (FailNextCreate)
            {
                FailNextCreate = false;
                throw new InvalidOperationException("upstream response body");
            }

            return Task.FromResult(201L);
        }
    }
}

public sealed class SqliteDeliveryAttemptRepositoryTests : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"GDK.TimeSync.Tests.{Guid.NewGuid():N}.db");

    [Fact]
    public async Task SaveAsync_UpdatesTheAttemptUsingOnlyTypedSafeFields()
    {
        var repository = new SqliteDeliveryAttemptRepository(new SqliteDatabase(databasePath));
        var itemId = Guid.NewGuid();

        await repository.SaveAsync(new(itemId, 101, null, DeliveryAttemptStatus.Failed, DeliveryFailureCode.TempoFailed, SlackDeliveryState.NotSupported));
        await repository.SaveAsync(new(itemId, 101, 201, DeliveryAttemptStatus.Succeeded, null, SlackDeliveryState.NotSupported));

        var attempt = await repository.GetAsync(itemId);

        Assert.Equal(new DeliveryAttempt(itemId, 101, 201, DeliveryAttemptStatus.Succeeded, null, SlackDeliveryState.NotSupported), attempt);
    }

    [Fact]
    public async Task OpenConnectionAsync_AppliesDeliverySchemaIdempotently()
    {
        var database = new SqliteDatabase(databasePath);

        await using (await database.OpenConnectionAsync()) { }
        await using (await database.OpenConnectionAsync()) { }

        Assert.Null(await new SqliteDeliveryAttemptRepository(database).GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task OpenConnectionAsync_CreatesOnlySafeDeliveryAttemptColumns()
    {
        var database = new SqliteDatabase(databasePath);
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info('delivery_attempts') ORDER BY cid";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(0));

        Assert.Equal(
            ["planned_work_item_id", "toggl_entry_id", "tempo_worklog_id", "status", "failure_code", "slack_state"],
            columns);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (File.Exists(databasePath))
            File.Delete(databasePath);
        return Task.CompletedTask;
    }
}
