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
    public async Task PostAsync_DoesNotRetryAnAmbiguousTempoWrite()
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
        Assert.Equal(1, jira.LookupCount);
        Assert.Equal(1, tempo.CreateCount);
        Assert.Equal(DeliveryAttemptStatus.Failed, Assert.Single(recovered.Attempts).Status);
        Assert.Equal(DeliveryFailureCode.TempoFailed, Assert.Single(recovered.Attempts).FailureCode);
    }

    [Fact]
    public async Task PostAsync_ClaimsAnItemBeforeWritingSoConcurrentCallsCreateOneEntry()
    {
        var item = CreateItem();
        var attempts = new InMemoryDeliveryAttemptRepository();
        var toggl = new BlockingTogglClient();
        var coordinator = new PostAllCoordinator(toggl, new RecordingJiraClient([]), new RecordingTempoClient([]), attempts);

        var first = coordinator.PostAsync(DailyPlan.Create(item.Day, [item]));
        await toggl.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = await coordinator.PostAsync(DailyPlan.Create(item.Day, [item]));
        toggl.Complete.SetResult(101);
        var completed = await first;

        Assert.Equal(1, toggl.CreateCount);
        Assert.Equal(DeliveryAttemptStatus.Succeeded, Assert.Single(completed.Attempts).Status);
        Assert.Equal(DeliveryAttemptStatus.ReconciliationRequired, Assert.Single(second.Attempts).Status);
    }

    [Fact]
    public async Task PostAsync_RequiresManualReconciliationForAnExistingInProgressClaim()
    {
        var item = CreateItem();
        var attempts = new InMemoryDeliveryAttemptRepository();
        await attempts.SaveAsync(new(item.Id, null, null, DeliveryAttemptStatus.InProgress, null, SlackDeliveryState.NotSupported));
        var toggl = new RecordingTogglClient([]);
        var jira = new RecordingJiraClient([]);
        var tempo = new RecordingTempoClient([]);
        var coordinator = new PostAllCoordinator(toggl, jira, tempo, attempts);

        var result = await coordinator.PostAsync(DailyPlan.Create(item.Day, [item]));

        var attempt = Assert.Single(result.Attempts);
        Assert.Equal(DeliveryAttemptStatus.ReconciliationRequired, attempt.Status);
        Assert.Equal(DeliveryFailureCode.PersistenceFailed, attempt.FailureCode);
        Assert.Equal(0, toggl.CreateCount);
        Assert.Equal(0, jira.LookupCount);
        Assert.Equal(0, tempo.CreateCount);
    }

    [Fact]
    public async Task PostAsync_ReportsPersistenceFailureAfterTogglWithoutDiscardingItsKnownId()
    {
        var item = CreateItem();
        var attempts = new InMemoryDeliveryAttemptRepository
        {
            FailuresRemaining = 1,
            FailWhen = attempt => attempt.TogglEntryId is not null && attempt.TempoWorklogId is null
        };
        var toggl = new RecordingTogglClient([]);
        var jira = new RecordingJiraClient([]);
        var tempo = new RecordingTempoClient([]);
        var coordinator = new PostAllCoordinator(toggl, jira, tempo, attempts);

        var result = await coordinator.PostAsync(DailyPlan.Create(item.Day, [item]));
        var retried = await coordinator.PostAsync(DailyPlan.Create(item.Day, [item]));

        var attempt = Assert.Single(result.Attempts);
        Assert.Equal(101, attempt.TogglEntryId);
        Assert.Equal(DeliveryAttemptStatus.ReconciliationRequired, attempt.Status);
        Assert.Equal(DeliveryFailureCode.PersistenceFailed, attempt.FailureCode);
        Assert.Equal(1, toggl.CreateCount);
        Assert.Equal(0, jira.LookupCount);
        Assert.Equal(0, tempo.CreateCount);
        Assert.Equal(attempt, Assert.Single(retried.Attempts));
        Assert.Equal(attempt, await attempts.GetAsync(item.Id));
    }

    [Fact]
    public async Task PostAsync_ReportsPersistenceFailureAfterTempoWithoutDiscardingKnownIds()
    {
        var item = CreateItem();
        var attempts = new InMemoryDeliveryAttemptRepository
        {
            FailuresRemaining = 1,
            FailWhen = attempt => attempt.TempoWorklogId is not null
        };
        var toggl = new RecordingTogglClient([]);
        var jira = new RecordingJiraClient([]);
        var tempo = new RecordingTempoClient([]);
        var coordinator = new PostAllCoordinator(toggl, jira, tempo, attempts);

        var result = await coordinator.PostAsync(DailyPlan.Create(item.Day, [item]));
        var retried = await coordinator.PostAsync(DailyPlan.Create(item.Day, [item]));

        var attempt = Assert.Single(result.Attempts);
        Assert.Equal(101, attempt.TogglEntryId);
        Assert.Equal(201, attempt.TempoWorklogId);
        Assert.Equal(DeliveryAttemptStatus.ReconciliationRequired, attempt.Status);
        Assert.Equal(DeliveryFailureCode.PersistenceFailed, attempt.FailureCode);
        Assert.Equal(1, toggl.CreateCount);
        Assert.Equal(1, jira.LookupCount);
        Assert.Equal(1, tempo.CreateCount);
        Assert.Equal(attempt, Assert.Single(retried.Attempts));
        Assert.Equal(attempt, await attempts.GetAsync(item.Id));
    }

    [Fact]
    public async Task PostAsync_PersistsCancellationWhenTheInitialReadIsCancelled()
    {
        var item = CreateItem();
        using var cancellation = new CancellationTokenSource();
        var attempts = new InMemoryDeliveryAttemptRepository { CancelInitialRead = true, OnRead = cancellation.Cancel };
        var coordinator = new PostAllCoordinator(new RecordingTogglClient([]), new RecordingJiraClient([]), new RecordingTempoClient([]), attempts);

        var result = await coordinator.PostAsync(DailyPlan.Create(item.Day, [item]), cancellation.Token);

        var attempt = Assert.Single(result.Attempts);
        Assert.Equal(DeliveryAttemptStatus.Cancelled, attempt.Status);
        Assert.Equal(DeliveryFailureCode.Cancelled, attempt.FailureCode);
        Assert.Equal(attempt, await attempts.GetAsync(item.Id));
    }

    [Fact]
    public async Task PostAsync_KeepsTogglIdForLaterReconciliationWhenBothPersistenceWritesFail()
    {
        var item = CreateItem();
        var attempts = new InMemoryDeliveryAttemptRepository
        {
            FailuresRemaining = 2,
            FailWhen = attempt => attempt.TogglEntryId is not null && attempt.TempoWorklogId is null
        };
        var toggl = new RecordingTogglClient([]);
        var jira = new RecordingJiraClient([]);
        var tempo = new RecordingTempoClient([]);
        var coordinator = new PostAllCoordinator(toggl, jira, tempo, attempts);

        var unavailable = await coordinator.PostAsync(DailyPlan.Create(item.Day, [item]));

        var pending = Assert.Single(unavailable.Attempts);
        Assert.Equal(DeliveryAttemptStatus.ReconciliationRequired, pending.Status);
        Assert.Equal(DeliveryFailureCode.PersistenceFailed, pending.FailureCode);
        Assert.Equal(101, pending.TogglEntryId);
        Assert.Equal(
            new DeliveryAttempt(item.Id, null, null, DeliveryAttemptStatus.InProgress, null, SlackDeliveryState.NotSupported),
            await attempts.GetAsync(item.Id));
        var recovered = await coordinator.PostAsync(DailyPlan.Create(item.Day, [item]));
        Assert.Equal(pending, Assert.Single(recovered.Attempts));
        Assert.Equal(pending, await attempts.GetAsync(item.Id));
        Assert.Equal(1, toggl.CreateCount);
        Assert.Equal(0, jira.LookupCount);
        Assert.Equal(0, tempo.CreateCount);
    }

    [Fact]
    public async Task PostAsync_KeepsKnownIdWhenCancellationPersistenceFails()
    {
        var item = CreateItem();
        using var cancellation = new CancellationTokenSource();
        var attempts = new InMemoryDeliveryAttemptRepository
        {
            FailuresRemaining = 2,
            FailWhen = attempt => attempt.TogglEntryId is not null
        };
        var toggl = new RecordingTogglClient([], onCreated: cancellation.Cancel);
        var jira = new RecordingJiraClient([]);
        var tempo = new RecordingTempoClient([]);
        var coordinator = new PostAllCoordinator(toggl, jira, tempo, attempts);

        var unavailable = await coordinator.PostAsync(DailyPlan.Create(item.Day, [item]), cancellation.Token);
        var recovered = await coordinator.PostAsync(DailyPlan.Create(item.Day, [item]));

        var pending = Assert.Single(unavailable.Attempts);
        Assert.Equal(DeliveryAttemptStatus.ReconciliationRequired, pending.Status);
        Assert.Equal(DeliveryFailureCode.PersistenceFailed, pending.FailureCode);
        Assert.Equal(101, pending.TogglEntryId);
        Assert.Equal(pending, Assert.Single(recovered.Attempts));
        Assert.Equal(pending, await attempts.GetAsync(item.Id));
        Assert.Equal(1, toggl.CreateCount);
        Assert.Equal(0, jira.LookupCount);
        Assert.Equal(0, tempo.CreateCount);
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
        private readonly Lock gate = new();

        public Func<DeliveryAttempt, bool>? FailWhen { get; init; }
        public int FailuresRemaining { get; set; }
        public bool CancelInitialRead { get; init; }
        public Action? OnRead { get; init; }

        public Task<DeliveryAttempt?> GetAsync(Guid plannedWorkItemId, CancellationToken cancellationToken = default)
        {
            OnRead?.Invoke();
            if (CancelInitialRead && cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);

            lock (gate)
                return Task.FromResult(attempts.GetValueOrDefault(plannedWorkItemId));
        }

        public Task<DeliveryAttemptClaim> ClaimAsync(Guid plannedWorkItemId, CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                if (!attempts.TryGetValue(plannedWorkItemId, out var existing))
                {
                    var created = new DeliveryAttempt(plannedWorkItemId, null, null, DeliveryAttemptStatus.InProgress, null, SlackDeliveryState.NotSupported);
                    attempts[plannedWorkItemId] = created;
                    return Task.FromResult(new DeliveryAttemptClaim(created, true));
                }

                return Task.FromResult(new DeliveryAttemptClaim(existing, false));
            }
        }

        public Task SaveAsync(DeliveryAttempt attempt, CancellationToken cancellationToken = default)
        {
            if (FailuresRemaining > 0 && FailWhen?.Invoke(attempt) is true)
            {
                FailuresRemaining--;
                throw new InvalidOperationException("persistence unavailable");
            }

            lock (gate)
                attempts[attempt.PlannedWorkItemId] = attempt;
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingTogglClient : IPlannedItemTogglClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<long> Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CreateCount { get; private set; }

        public Task<long> CreateAsync(PlannedWorkItem item, CancellationToken cancellationToken = default)
        {
            CreateCount++;
            Started.TrySetResult();
            return Complete.Task;
        }
    }

    private sealed class RecordingTogglClient(List<string> events, Action? onCreate = null, Action? onCreated = null) : IPlannedItemTogglClient
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
            onCreated?.Invoke();
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

    [Fact]
    public async Task ClaimAsync_AtomicallyAllowsOnlyOneWriter()
    {
        var repository = new SqliteDeliveryAttemptRepository(new SqliteDatabase(databasePath));
        var itemId = Guid.NewGuid();
        await repository.GetAsync(Guid.NewGuid());

        var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => repository.ClaimAsync(itemId)));

        Assert.Equal(1, claims.Count(claim => claim.IsAcquired));
        Assert.All(claims, claim => Assert.Equal(itemId, claim.Attempt.PlannedWorkItemId));
    }

    [Fact]
    public async Task ClaimAsync_InitializesSchemaAndClaimsOnceUnderConcurrentFirstUse()
    {
        var itemId = Guid.NewGuid();

        var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            new SqliteDeliveryAttemptRepository(new SqliteDatabase(databasePath)).ClaimAsync(itemId)));

        Assert.Equal(1, claims.Count(claim => claim.IsAcquired));
        Assert.All(claims, claim => Assert.Equal(itemId, claim.Attempt.PlannedWorkItemId));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (File.Exists(databasePath))
            File.Delete(databasePath);
        return Task.CompletedTask;
    }
}
