using System.Net;
using System.Net.Http.Json;
using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Jira;
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
        Assert.Equal(2, attempts.SaveCount);
    }

    [Fact]
    public async Task CreateAndVerifyTempoAsync_requires_reconciliation_when_readback_does_not_match()
    {
        var item = CreateItem();
        var calls = new List<string>();
        var clients = new RecordingIntegrationClientFactory(calls) { ReturnMismatchedTempoRead = true };
        var attempts = new RecordingAttemptRepository(new DeliveryAttempt(item.Id, 123, null, DeliveryAttemptStatus.InProgress, null, SlackDeliveryState.NotSupported));
        var service = CreateService(clients, attempts);

        var result = await service.CreateAndVerifyTempoAsync(item);

        Assert.Equal(DeliveryAttemptStatus.ReconciliationRequired, result.Attempt.Status);
        Assert.Equal(456L, result.Attempt.TempoWorklogId);
        Assert.Equal(["JiraGet", "TempoCreate", "TempoRead"], calls);
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
        Assert.Equal(2, attempts.SaveCount);
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

    private static LiveIntegrationValidationService CreateService(RecordingIntegrationClientFactory clients, RecordingAttemptRepository attempts) =>
        new(clients, new FixedSettingsStore(), attempts);

    private static PlannedWorkItem CreateItem() => PlannedWorkItem.Create(
        new DateOnly(2026, 8, 14),
        name: "Validation work",
        jiraIssueKey: "GDK-42",
        comment: "Validate integrations",
        duration: TimeSpan.FromMinutes(30),
        start: new TimeOnly(9, 0));

    private sealed class FixedSettingsStore : IUserSettingsStore
    {
        public UserSettings Load() => new() { TogglWorkspaceId = 77, JiraUser = "planner" };
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

    private sealed class RecordingIntegrationClientFactory(List<string> calls) : IIntegrationClientFactory
    {
        public int TogglClientCreations { get; private set; }
        public int TempoClientCreations { get; private set; }
        public bool FailTogglCreate { get; init; }
        public bool ReturnMismatchedTempoRead { get; init; }
        public string? JiraFailureDetail { get; init; }

        public Task<ITogglClient> CreateTogglAsync(CancellationToken cancellationToken = default)
        {
            TogglClientCreations++;
            return Task.FromResult<ITogglClient>(new TogglClient(CreateHttpClient(new RecordingHandler(calls, IntegrationTarget.Toggl, FailTogglCreate, null)), new TogglOptions { BaseUrl = "https://validation.example.test/", ApiToken = "unit-token" }));
        }

        public Task<JiraClient> CreateJiraAsync(CancellationToken cancellationToken = default) => Task.FromResult(new JiraClient(
            CreateHttpClient(new RecordingHandler(calls, IntegrationTarget.Jira, false, JiraFailureDetail)),
            new JiraOptions { BaseUrl = "https://validation.example.test/", PersonalAccessToken = "unit-token" },
            new IssueKeyValidator(new IssueKeyValidationOptions())));

        public Task<TempoClient> CreateTempoAsync(CancellationToken cancellationToken = default)
        {
            TempoClientCreations++;
            return Task.FromResult(new TempoClient(CreateHttpClient(new RecordingHandler(calls, IntegrationTarget.Tempo, false, null, ReturnMismatchedTempoRead)), new TempoOptions { BaseUrl = "https://validation.example.test/", PersonalAccessToken = "unit-token" }));
        }

        private static HttpClient CreateHttpClient(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("https://validation.example.test/") };
    }

    private enum IntegrationTarget { Toggl, Jira, Tempo }

    private sealed class RecordingHandler(List<string> calls, IntegrationTarget target, bool failCreate, string? jiraFailureDetail, bool returnMismatchedTempoRead = false) : HttpMessageHandler
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
                "TempoRead" => Json(new TempoWorklog(returnMismatchedTempoRead ? 457 : 456, "planner", "jira-42", new DateTime(2026, 8, 14, 9, 0, 0), returnMismatchedTempoRead ? 1799 : 1800, "Validate integrations")),
                _ => throw new ArgumentOutOfRangeException()
            });
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
