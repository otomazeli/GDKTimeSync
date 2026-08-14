using System.Net;
using System.Net.Http.Json;
using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Jira;
using GDK.TimeSync.Persistence;
using GDK.TimeSync.Tempo;
using GDK.TimeSync.Toggl;

namespace GDK.TimeSync.Tests;

public sealed class LiveIntegrationValidationServiceTests
{
    [Fact]
    public async Task CreateTogglAsync_creates_only_toggl_and_persists_its_safe_id()
    {
        var calls = new List<string>();
        var clients = new RecordingIntegrationClientFactory(calls);
        var attempts = new RecordingAttemptRepository();
        var service = CreateService(clients, attempts);

        var result = await service.CreateTogglAsync(CreateItem());

        Assert.Equal(LiveValidationStep.Toggl, result.Step);
        Assert.Equal(DeliveryAttemptStatus.InProgress, result.Attempt.Status);
        Assert.Equal(123L, result.Attempt.TogglEntryId);
        Assert.Null(result.Attempt.TempoWorklogId);
        Assert.Equal(["TogglCreate"], calls);
        Assert.Equal(1, attempts.ClaimCount);
        Assert.Equal(1, attempts.SaveCount);
    }

    [Fact]
    public async Task ValidateJiraAsync_is_read_only_and_does_not_create_other_clients()
    {
        var calls = new List<string>();
        var clients = new RecordingIntegrationClientFactory(calls);
        var attempts = new RecordingAttemptRepository();
        var service = CreateService(clients, attempts);

        var result = await service.ValidateJiraAsync(CreateItem());

        Assert.Equal(LiveValidationStep.Jira, result.Step);
        Assert.Equal(DeliveryAttemptStatus.Succeeded, result.Attempt.Status);
        Assert.Equal(["JiraGet"], calls);
        Assert.Equal(0, attempts.ClaimCount);
        Assert.Equal(0, attempts.SaveCount);
        Assert.Equal(0, clients.TogglClientCreations + clients.TempoClientCreations);
    }

    [Fact]
    public async Task CreateAndVerifyTempoAsync_revalidates_jira_then_creates_and_reads_back_tempo()
    {
        var item = CreateItem();
        var calls = new List<string>();
        var clients = new RecordingIntegrationClientFactory(calls);
        var attempts = new RecordingAttemptRepository(new DeliveryAttempt(item.Id, 123, null, DeliveryAttemptStatus.InProgress, null, SlackDeliveryState.NotSupported));
        var service = CreateService(clients, attempts);

        var result = await service.CreateAndVerifyTempoAsync(item);

        Assert.Equal(LiveValidationStep.Tempo, result.Step);
        Assert.Equal(DeliveryAttemptStatus.Succeeded, result.Attempt.Status);
        Assert.Equal(456L, result.Attempt.TempoWorklogId);
        Assert.Equal(["JiraGet", "TempoCreate", "TempoRead"], calls);
        Assert.Equal(0, attempts.ClaimCount);
        Assert.Equal(3, attempts.SaveCount);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CreateAndVerifyTempoAsync_requires_reconciliation_when_readback_id_or_duration_does_not_match(bool mismatchId, bool mismatchDuration)
    {
        var item = CreateItem();
        var calls = new List<string>();
        var clients = new RecordingIntegrationClientFactory(calls) { ReturnMismatchedTempoReadId = mismatchId, ReturnMismatchedTempoReadDuration = mismatchDuration };
        var attempts = new RecordingAttemptRepository(new DeliveryAttempt(item.Id, 123, null, DeliveryAttemptStatus.InProgress, null, SlackDeliveryState.NotSupported));
        var service = CreateService(clients, attempts);

        var result = await service.CreateAndVerifyTempoAsync(item);

        Assert.Equal(DeliveryAttemptStatus.ReconciliationRequired, result.Attempt.Status);
        Assert.Equal(456L, result.Attempt.TempoWorklogId);
        Assert.Equal(["JiraGet", "TempoCreate", "TempoRead"], calls);
    }

    [Fact]
    public async Task CreateAndVerifyTempoAsync_reads_existing_tempo_id_without_creating_another_worklog()
    {
        var item = CreateItem();
        var calls = new List<string>();
        var existing = new DeliveryAttempt(item.Id, 123, 456, DeliveryAttemptStatus.InProgress, null, SlackDeliveryState.NotSupported);
        var attempts = new RecordingAttemptRepository(existing);

        var result = await CreateService(new RecordingIntegrationClientFactory(calls), attempts).CreateAndVerifyTempoAsync(item);

        Assert.Equal(DeliveryAttemptStatus.Succeeded, result.Attempt.Status);
        Assert.Equal(456L, result.Attempt.TempoWorklogId);
        Assert.Equal(["TempoRead"], calls);
        Assert.Equal(1, attempts.SaveCount);
    }

    [Theory]
    [InlineData(null, DeliveryAttemptStatus.InProgress)]
    [InlineData(123L, DeliveryAttemptStatus.Succeeded)]
    public async Task CreateAndVerifyTempoAsync_blocks_missing_or_terminal_toggl_before_client_creation(long? togglEntryId, DeliveryAttemptStatus status)
    {
        var item = CreateItem();
        var calls = new List<string>();
        var clients = new RecordingIntegrationClientFactory(calls);
        var existing = new DeliveryAttempt(item.Id, togglEntryId, null, status, null, SlackDeliveryState.NotSupported);
        var attempts = new RecordingAttemptRepository(existing);
        var service = CreateService(clients, attempts);

        var result = await service.CreateAndVerifyTempoAsync(item);

        Assert.Equal(existing, result.Attempt);
        Assert.Equal("Tempo requires a non-terminal Toggl entry.", result.SafeMessage);
        Assert.Empty(calls);
        Assert.Equal(0, attempts.SaveCount);
    }

    [Fact]
    public async Task CreateTogglAsync_marks_ambiguous_write_failure_for_reconciliation_without_retrying()
    {
        var calls = new List<string>();
        var clients = new RecordingIntegrationClientFactory(calls) { FailTogglCreate = true };
        var attempts = new RecordingAttemptRepository();
        var service = CreateService(clients, attempts);
        var item = CreateItem();

        var result = await service.CreateTogglAsync(item);
        var repeated = await service.CreateTogglAsync(item);

        Assert.Equal(DeliveryAttemptStatus.ReconciliationRequired, result.Attempt.Status);
        Assert.Equal(DeliveryFailureCode.TogglFailed, result.Attempt.FailureCode);
        Assert.Equal(result.Attempt, repeated.Attempt);
        Assert.Equal(["TogglCreate"], calls);
        Assert.Equal(2, attempts.ClaimCount);
        Assert.Equal(1, attempts.SaveCount);
    }

    [Fact]
    public async Task CreateTogglAsync_keeps_known_id_when_post_write_persistence_fails()
    {
        var attempts = new RecordingAttemptRepository
        {
            FailuresRemaining = 1,
            FailWhen = value => value.TogglEntryId is not null && value.Status == DeliveryAttemptStatus.InProgress
        };
        var result = await CreateService(new RecordingIntegrationClientFactory([]), attempts).CreateTogglAsync(CreateItem());

        Assert.Equal(123L, result.Attempt.TogglEntryId);
        Assert.Equal(DeliveryAttemptStatus.ReconciliationRequired, result.Attempt.Status);
        Assert.Equal(DeliveryFailureCode.TogglFailed, result.Attempt.FailureCode);
        Assert.Equal(2, attempts.SaveCount);
    }

    [Fact]
    public async Task CreateTogglAsync_keeps_a_no_resend_barrier_when_both_post_write_persistence_saves_fail()
    {
        var calls = new List<string>();
        var attempts = new RecordingAttemptRepository
        {
            FailuresRemaining = 2,
            FailWhen = value => value.TogglEntryId is not null
        };
        var service = CreateService(new RecordingIntegrationClientFactory(calls), attempts);

        var failed = await service.CreateTogglAsync(CreateItem());
        var repeated = await service.CreateTogglAsync(CreateItem() with { Id = failed.Attempt.PlannedWorkItemId });

        Assert.Equal(DeliveryAttemptStatus.ReconciliationRequired, failed.Attempt.Status);
        Assert.Equal(DeliveryFailureCode.PersistenceFailed, failed.Attempt.FailureCode);
        Assert.Equal(failed.Attempt, repeated.Attempt);
        Assert.Equal(["TogglCreate"], calls);
        Assert.Equal(2, attempts.SaveCount);
    }

    [Fact]
    public async Task CreateAndVerifyTempoAsync_keeps_a_no_resend_barrier_when_both_post_write_persistence_saves_fail()
    {
        var item = CreateItem();
        var databasePath = Path.Combine(Path.GetTempPath(), $"GDK.TimeSync.LiveValidation.{Guid.NewGuid():N}.db");
        try
        {
            var firstRepository = new SqliteDeliveryAttemptRepository(new SqliteDatabase(databasePath));
            await firstRepository.SaveAsync(new DeliveryAttempt(item.Id, 123, null, DeliveryAttemptStatus.InProgress, null, SlackDeliveryState.NotSupported));
            var firstAttempts = new FailureInjectingAttemptRepository(firstRepository)
            {
                FailuresRemaining = 2,
                FailWhen = value => value.TempoWorklogId is not null
            };
            var firstCalls = new List<string>();

            var failed = await CreateService(new RecordingIntegrationClientFactory(firstCalls), firstAttempts).CreateAndVerifyTempoAsync(item);
            var resumedCalls = new List<string>();
            var repeated = await CreateService(new RecordingIntegrationClientFactory(resumedCalls), new SqliteDeliveryAttemptRepository(new SqliteDatabase(databasePath))).CreateAndVerifyTempoAsync(item);

            Assert.Equal(DeliveryAttemptStatus.ReconciliationRequired, failed.Attempt.Status);
            Assert.Equal(DeliveryFailureCode.PersistenceFailed, failed.Attempt.FailureCode);
            Assert.Equal(["JiraGet", "TempoCreate"], firstCalls);
            Assert.Equal(DeliveryAttemptStatus.ReconciliationRequired, repeated.Attempt.Status);
            Assert.Equal(DeliveryFailureCode.PersistenceFailed, repeated.Attempt.FailureCode);
            Assert.Empty(resumedCalls);
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task CreateAndVerifyTempoAsync_disposes_created_client_when_durable_marker_save_fails()
    {
        var item = CreateItem();
        var calls = new List<string>();
        var attempts = new RecordingAttemptRepository(new DeliveryAttempt(item.Id, 123, null, DeliveryAttemptStatus.InProgress, null, SlackDeliveryState.NotSupported))
        {
            FailuresRemaining = 1,
            FailWhen = value => value is { TempoWorklogId: null, Status: DeliveryAttemptStatus.ReconciliationRequired }
        };
        var clients = new RecordingIntegrationClientFactory(calls);

        var result = await CreateService(clients, attempts).CreateAndVerifyTempoAsync(item);

        Assert.Equal(DeliveryAttemptStatus.ReconciliationRequired, result.Attempt.Status);
        Assert.Equal(DeliveryFailureCode.PersistenceFailed, result.Attempt.FailureCode);
        Assert.Equal(["JiraGet"], calls);
        Assert.True(Assert.Single(clients.TempoHttpClients).WasDisposed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CreateTogglAsync_blocks_missing_or_inconsistent_selected_times_before_claim_or_client_creation(int timeProblem)
    {
        var item = InvalidTimingItem(timeProblem);
        var calls = new List<string>();
        var clients = new RecordingIntegrationClientFactory(calls);
        var attempts = new RecordingAttemptRepository();

        var result = await CreateService(clients, attempts).CreateTogglAsync(item);

        Assert.Equal(DeliveryAttemptStatus.Failed, result.Attempt.Status);
        Assert.Empty(calls);
        Assert.Equal(0, attempts.ClaimCount);
        Assert.Equal(0, attempts.SaveCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CreateAndVerifyTempoAsync_blocks_missing_or_inconsistent_selected_times_before_client_creation(int timeProblem)
    {
        var item = InvalidTimingItem(timeProblem);
        var calls = new List<string>();
        var clients = new RecordingIntegrationClientFactory(calls);
        var attempts = new RecordingAttemptRepository(new DeliveryAttempt(item.Id, 123, null, DeliveryAttemptStatus.InProgress, null, SlackDeliveryState.NotSupported));

        var result = await CreateService(clients, attempts).CreateAndVerifyTempoAsync(item);

        Assert.Equal(DeliveryAttemptStatus.Failed, result.Attempt.Status);
        Assert.Empty(calls);
        Assert.Equal(0, attempts.SaveCount);
    }

    [Fact]
    public async Task CreateTogglAsync_prewrite_cancellation_is_cancelled_before_claim_or_client_creation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = new List<string>();
        var attempts = new RecordingAttemptRepository();

        var result = await CreateService(new RecordingIntegrationClientFactory(calls), attempts).CreateTogglAsync(CreateItem(), cancellation.Token);

        Assert.Equal(DeliveryAttemptStatus.Cancelled, result.Attempt.Status);
        Assert.Empty(calls);
        Assert.Equal(0, attempts.ClaimCount);
        Assert.Equal(0, attempts.SaveCount);
    }

    [Fact]
    public async Task CreateAndVerifyTempoAsync_prewrite_cancellation_is_cancelled_before_client_creation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var item = CreateItem();
        var calls = new List<string>();
        var attempts = new RecordingAttemptRepository(new DeliveryAttempt(item.Id, 123, null, DeliveryAttemptStatus.InProgress, null, SlackDeliveryState.NotSupported));

        var result = await CreateService(new RecordingIntegrationClientFactory(calls), attempts).CreateAndVerifyTempoAsync(item, cancellation.Token);

        Assert.Equal(DeliveryAttemptStatus.Cancelled, result.Attempt.Status);
        Assert.Empty(calls);
        Assert.Equal(0, attempts.SaveCount);
    }

    [Fact]
    public async Task CreateTogglAsync_preflight_and_factory_failures_are_failed_without_reconciliation()
    {
        var preflightCalls = new List<string>();
        var preflight = await CreateService(new RecordingIntegrationClientFactory(preflightCalls), new RecordingAttemptRepository(), new UserSettings { JiraUser = "planner" }).CreateTogglAsync(CreateItem());
        var factoryCalls = new List<string>();
        var factory = await CreateService(new RecordingIntegrationClientFactory(factoryCalls) { FailTogglFactory = true }, new RecordingAttemptRepository()).CreateTogglAsync(CreateItem());

        Assert.Equal(DeliveryAttemptStatus.Failed, preflight.Attempt.Status);
        Assert.Equal(DeliveryAttemptStatus.Failed, factory.Attempt.Status);
        Assert.NotEqual(DeliveryAttemptStatus.ReconciliationRequired, factory.Attempt.Status);
        Assert.Empty(preflightCalls);
        Assert.Empty(factoryCalls);
    }

    [Fact]
    public async Task CreateAndVerifyTempoAsync_factory_failure_is_failed_without_reconciliation()
    {
        var item = CreateItem();
        var calls = new List<string>();
        var clients = new RecordingIntegrationClientFactory(calls) { FailTempoFactory = true };
        var attempts = new RecordingAttemptRepository(new DeliveryAttempt(item.Id, 123, null, DeliveryAttemptStatus.InProgress, null, SlackDeliveryState.NotSupported));

        var result = await CreateService(clients, attempts).CreateAndVerifyTempoAsync(item);

        Assert.Equal(DeliveryAttemptStatus.Failed, result.Attempt.Status);
        Assert.NotEqual(DeliveryAttemptStatus.ReconciliationRequired, result.Attempt.Status);
        Assert.Equal(["JiraGet"], calls);
        Assert.Equal(0, attempts.SaveCount);
    }

    [Fact]
    public async Task CreateAndVerifyTempoAsync_marks_post_write_cancellation_for_reconciliation_without_deleting()
    {
        var item = CreateItem();
        using var cancellation = new CancellationTokenSource();
        var calls = new List<string>();
        var initial = new DeliveryAttempt(item.Id, 123, null, DeliveryAttemptStatus.InProgress, null, SlackDeliveryState.NotSupported);
        var clients = new RecordingIntegrationClientFactory(calls);
        var attempts = new RecordingAttemptRepository(initial)
        {
            OnSave = value =>
            {
                if (value is { TempoWorklogId: not null, Status: DeliveryAttemptStatus.InProgress })
                    cancellation.Cancel();
            }
        };
        var service = CreateService(clients, attempts);

        var result = await service.CreateAndVerifyTempoAsync(item, cancellation.Token);
        var repeated = await service.CreateAndVerifyTempoAsync(item);

        Assert.Equal(DeliveryAttemptStatus.ReconciliationRequired, result.Attempt.Status);
        Assert.Equal(DeliveryFailureCode.Cancelled, result.Attempt.FailureCode);
        Assert.Equal(456L, result.Attempt.TempoWorklogId);
        Assert.Equal(result.Attempt, repeated.Attempt);
        Assert.Equal(["JiraGet", "TempoCreate", "TempoRead"], calls);
        Assert.Equal(3, attempts.SaveCount);
    }

    [Fact]
    public async Task CreateTogglAsync_contains_post_write_disposal_failure_as_reconciliation()
    {
        var calls = new List<string>();
        var result = await CreateService(new RecordingIntegrationClientFactory(calls) { ThrowOnTogglDispose = true }, new RecordingAttemptRepository()).CreateTogglAsync(CreateItem());

        Assert.Equal(DeliveryAttemptStatus.ReconciliationRequired, result.Attempt.Status);
        Assert.Equal(123L, result.Attempt.TogglEntryId);
        Assert.Equal(["TogglCreate"], calls);
    }

    [Fact]
    public async Task CreateAndVerifyTempoAsync_contains_post_write_disposal_failure_as_reconciliation()
    {
        var item = CreateItem();
        var calls = new List<string>();
        var attempts = new RecordingAttemptRepository(new DeliveryAttempt(item.Id, 123, null, DeliveryAttemptStatus.InProgress, null, SlackDeliveryState.NotSupported));

        var result = await CreateService(new RecordingIntegrationClientFactory(calls) { ThrowOnTempoDispose = true }, attempts).CreateAndVerifyTempoAsync(item);

        Assert.Equal(DeliveryAttemptStatus.ReconciliationRequired, result.Attempt.Status);
        Assert.Equal(456L, result.Attempt.TempoWorklogId);
        Assert.Equal(["JiraGet", "TempoCreate", "TempoRead"], calls);
    }

    [Fact]
    public async Task Results_and_safe_messages_do_not_include_failure_sentinel()
    {
        const string sentinel = "not-a-secret-validation-sentinel";
        var clients = new RecordingIntegrationClientFactory([]) { JiraFailureDetail = sentinel };
        var result = await CreateService(clients, new RecordingAttemptRepository()).ValidateJiraAsync(CreateItem());

        Assert.DoesNotContain(sentinel, result.SafeMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, result.ToString(), StringComparison.Ordinal);
    }

    private static LiveIntegrationValidationService CreateService(RecordingIntegrationClientFactory clients, IDeliveryAttemptRepository attempts, UserSettings? settings = null) =>
        new(clients, new FixedSettingsStore(settings), attempts);

    private static PlannedWorkItem CreateItem() => PlannedWorkItem.Create(
        new DateOnly(2026, 8, 14),
        name: "Validation work",
        jiraIssueKey: "GDK-42",
        comment: "Validate integrations",
        duration: TimeSpan.FromMinutes(30),
        start: new TimeOnly(9, 0),
        end: new TimeOnly(9, 30));

    private static PlannedWorkItem InvalidTimingItem(int timeProblem) => timeProblem switch
    {
        0 => CreateItem() with { Start = null },
        1 => CreateItem() with { End = null },
        2 => CreateItem() with { End = new TimeOnly(9, 45) },
        _ => throw new ArgumentOutOfRangeException(nameof(timeProblem))
    };

    private sealed class FixedSettingsStore(UserSettings? settings = null) : IUserSettingsStore
    {
        public UserSettings Load() => settings ?? new UserSettings { TogglWorkspaceId = 77, JiraUser = "planner" };
        public void Save(UserSettings settings) => throw new NotSupportedException();
    }

    private sealed class RecordingAttemptRepository(DeliveryAttempt? initial = null) : IDeliveryAttemptRepository
    {
        private DeliveryAttempt? attempt = initial;

        public Action<DeliveryAttempt>? OnSave { get; init; }
        public Func<DeliveryAttempt, bool>? FailWhen { get; init; }
        public int FailuresRemaining { get; set; }
        public int ClaimCount { get; private set; }
        public int SaveCount { get; private set; }

        public Task<DeliveryAttempt?> GetAsync(Guid plannedWorkItemId, CancellationToken cancellationToken = default) => Task.FromResult(attempt);
        public Task<IReadOnlyList<DeliveryAttempt>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DeliveryAttempt>>(attempt is null ? [] : [attempt]);

        public Task<DeliveryAttemptClaim> ClaimAsync(Guid plannedWorkItemId, CancellationToken cancellationToken = default)
        {
            ClaimCount++;
            if (attempt is not null)
                return Task.FromResult(new DeliveryAttemptClaim(attempt, false));

            attempt = new DeliveryAttempt(plannedWorkItemId, null, null, DeliveryAttemptStatus.InProgress, null, SlackDeliveryState.NotSupported);
            return Task.FromResult(new DeliveryAttemptClaim(attempt, true));
        }

        public Task SaveAsync(DeliveryAttempt value, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            if (FailuresRemaining > 0 && FailWhen?.Invoke(value) is true)
            {
                FailuresRemaining--;
                throw new InvalidOperationException();
            }
            attempt = value;
            OnSave?.Invoke(value);
            return Task.CompletedTask;
        }
    }

    private sealed class FailureInjectingAttemptRepository(IDeliveryAttemptRepository inner) : IDeliveryAttemptRepository
    {
        public Func<DeliveryAttempt, bool>? FailWhen { get; init; }
        public int FailuresRemaining { get; set; }

        public Task<DeliveryAttempt?> GetAsync(Guid plannedWorkItemId, CancellationToken cancellationToken = default) => inner.GetAsync(plannedWorkItemId, cancellationToken);
        public Task<IReadOnlyList<DeliveryAttempt>> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);
        public Task<DeliveryAttemptClaim> ClaimAsync(Guid plannedWorkItemId, CancellationToken cancellationToken = default) => inner.ClaimAsync(plannedWorkItemId, cancellationToken);

        public Task SaveAsync(DeliveryAttempt attempt, CancellationToken cancellationToken = default)
        {
            if (FailuresRemaining > 0 && FailWhen?.Invoke(attempt) is true)
            {
                FailuresRemaining--;
                throw new InvalidOperationException();
            }

            return inner.SaveAsync(attempt, cancellationToken);
        }
    }

    private sealed class RecordingIntegrationClientFactory(List<string> calls) : IIntegrationClientFactory
    {
        public List<ThrowingDisposeHttpClient> TempoHttpClients { get; } = [];
        public int TogglClientCreations { get; private set; }
        public int TempoClientCreations { get; private set; }
        public bool FailTogglCreate { get; init; }
        public bool ReturnMismatchedTempoReadId { get; init; }
        public bool ReturnMismatchedTempoReadDuration { get; init; }
        public bool FailTogglFactory { get; init; }
        public bool FailTempoFactory { get; init; }
        public bool ThrowOnTogglDispose { get; init; }
        public bool ThrowOnTempoDispose { get; init; }
        public string? JiraFailureDetail { get; init; }

        public Task<ITogglClient> CreateTogglAsync(CancellationToken cancellationToken = default)
        {
            if (FailTogglFactory)
                throw new InvalidOperationException();
            TogglClientCreations++;
            return Task.FromResult<ITogglClient>(new TogglClient(CreateHttpClient(new RecordingHandler(calls, IntegrationTarget.Toggl, FailTogglCreate, null), ThrowOnTogglDispose), new TogglOptions { BaseUrl = "https://validation.example.test/", ApiToken = "unit-token" }));
        }

        public Task<JiraClient> CreateJiraAsync(CancellationToken cancellationToken = default) => Task.FromResult(new JiraClient(
            CreateHttpClient(new RecordingHandler(calls, IntegrationTarget.Jira, false, JiraFailureDetail)),
            new JiraOptions { BaseUrl = "https://validation.example.test/", PersonalAccessToken = "unit-token" },
            new IssueKeyValidator(new IssueKeyValidationOptions())));

        public Task<TempoClient> CreateTempoAsync(CancellationToken cancellationToken = default)
        {
            if (FailTempoFactory)
                throw new InvalidOperationException();
            TempoClientCreations++;
            var httpClient = CreateHttpClient(new RecordingHandler(calls, IntegrationTarget.Tempo, false, null, ReturnMismatchedTempoReadId, ReturnMismatchedTempoReadDuration), ThrowOnTempoDispose);
            TempoHttpClients.Add(httpClient);
            return Task.FromResult(new TempoClient(httpClient, new TempoOptions { BaseUrl = "https://validation.example.test/", PersonalAccessToken = "unit-token" }));
        }

        private static ThrowingDisposeHttpClient CreateHttpClient(HttpMessageHandler handler, bool throwOnDispose = false) => new(handler, throwOnDispose) { BaseAddress = new Uri("https://validation.example.test/") };
    }

    private enum IntegrationTarget { Toggl, Jira, Tempo }

    private sealed class ThrowingDisposeHttpClient(HttpMessageHandler handler, bool throwOnDispose) : HttpClient(handler, disposeHandler: false)
    {
        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
            if (throwOnDispose) throw new InvalidOperationException();
        }
    }

    private sealed class RecordingHandler(List<string> calls, IntegrationTarget target, bool failCreate, string? jiraFailureDetail, bool returnMismatchedTempoReadId = false, bool returnMismatchedTempoReadDuration = false) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var call = (target, request.Method.Method, request.RequestUri!.AbsolutePath) switch
            {
                (IntegrationTarget.Toggl, "POST", _) => "TogglCreate",
                (IntegrationTarget.Jira, "GET", _) => "JiraGet",
                (IntegrationTarget.Tempo, "POST", _) => "TempoCreate",
                (IntegrationTarget.Tempo, "GET", _) => "TempoRead",
                _ => throw new Xunit.Sdk.XunitException($"Unexpected request: {request.Method} {request.RequestUri}")
            };
            calls.Add(call);

            if (failCreate && target == IntegrationTarget.Toggl)
                throw new HttpRequestException("safe failure category");
            if (jiraFailureDetail is not null && target == IntegrationTarget.Jira)
                throw new HttpRequestException(jiraFailureDetail);

            return Task.FromResult(call switch
            {
                "TogglCreate" => Json(new TogglTimeEntry { Id = 123, Description = "Validate integrations", Start = new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero), Stop = new DateTimeOffset(2026, 8, 14, 9, 30, 0, TimeSpan.Zero) }),
                "JiraGet" => Json(new { id = "jira-42", key = "GDK-42", fields = new { summary = "Validation" } }),
                "TempoCreate" => Json(new TempoWorklog(456, "planner", "jira-42", new DateTime(2026, 8, 14, 9, 0, 0), 1800, "Validate integrations")),
                "TempoRead" => Json(new TempoWorklog(returnMismatchedTempoReadId ? 457 : 456, "planner", "jira-42", new DateTime(2026, 8, 14, 9, 0, 0), returnMismatchedTempoReadDuration ? 1799 : 1800, "Validate integrations")),
                _ => throw new ArgumentOutOfRangeException()
            });
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
