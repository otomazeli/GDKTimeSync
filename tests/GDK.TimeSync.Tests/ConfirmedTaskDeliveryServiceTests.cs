using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Jira;
using GDK.TimeSync.Tempo;
using GDK.TimeSync.Toggl;

namespace GDK.TimeSync.Tests;

public sealed class ConfirmedTaskDeliveryServiceTests
{
    [Fact]
    public async Task DeliverConfirmedAsync_posts_a_one_item_plan_and_disposes_every_created_client()
    {
        var item = PlannedWorkItem.Create(new DateOnly(2026, 8, 13), "Planning", "CGM-1", "Reviewed work", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT", start: new TimeOnly(9, 0));
        var clients = new RecordingIntegrationClientFactory();
        var service = new ConfirmedTaskDeliveryService(
            clients,
            new FixedSettingsStore(new UserSettings { TogglWorkspaceId = 42, JiraUser = "planner" }),
            new InMemoryAttemptRepository());

        var attempt = await service.DeliverConfirmedAsync(item);

        Assert.Equal(item.Id, attempt.PlannedWorkItemId);
        Assert.Equal(DeliveryAttemptStatus.Succeeded, attempt.Status);
        Assert.Equal(1, clients.TogglRequests);
        Assert.Equal(1, clients.JiraIssueRequests);
        Assert.Equal(1, clients.TempoWorklogRequests);
        Assert.All(clients.CreatedClients, client => Assert.True(client.WasDisposed));
    }

    // Issue #13: Tempo answered a real delivery with
    // {"errors":{"worker":"User is invalid"}} because the worker came from a typed setting. Jira
    // knows who we are, so ask it -- the Delphi reference client reads `key` first, then `name`.
    [Fact]
    public async Task DeliverConfirmedAsync_resolves_the_tempo_worker_from_jira_when_none_is_configured()
    {
        var clients = new RecordingIntegrationClientFactory();
        clients.Handler.MyselfKey = "JIRAUSER4711";
        clients.Handler.MyselfName = "odimar.tomazeli";
        var service = new ConfirmedTaskDeliveryService(
            clients,
            new FixedSettingsStore(new UserSettings { TogglWorkspaceId = 42, JiraUser = "" }),
            new InMemoryAttemptRepository());

        var attempt = await service.DeliverConfirmedAsync(Item());

        Assert.Equal(DeliveryAttemptStatus.Succeeded, attempt.Status);
        Assert.Equal("JIRAUSER4711", clients.LastTempoWorker);
    }

    [Fact]
    public async Task DeliverConfirmedAsync_falls_back_to_the_jira_name_when_no_key_is_returned()
    {
        var clients = new RecordingIntegrationClientFactory();
        clients.Handler.MyselfName = "odimar.tomazeli";
        var service = new ConfirmedTaskDeliveryService(
            clients,
            new FixedSettingsStore(new UserSettings { TogglWorkspaceId = 42, JiraUser = "" }),
            new InMemoryAttemptRepository());

        await service.DeliverConfirmedAsync(Item());

        Assert.Equal("odimar.tomazeli", clients.LastTempoWorker);
    }

    // A typed value is an explicit override: whoever set it did so because resolution was not enough.
    [Fact]
    public async Task DeliverConfirmedAsync_prefers_a_configured_worker_and_does_not_ask_jira()
    {
        var clients = new RecordingIntegrationClientFactory();
        clients.Handler.MyselfKey = "JIRAUSER4711";
        var service = new ConfirmedTaskDeliveryService(
            clients,
            new FixedSettingsStore(new UserSettings { TogglWorkspaceId = 42, JiraUser = "explicit.override" }),
            new InMemoryAttemptRepository());

        await service.DeliverConfirmedAsync(Item());

        Assert.Equal("explicit.override", clients.LastTempoWorker);
        Assert.Equal(0, clients.JiraMyselfRequests);
    }

    // Nothing configured and nothing resolvable must fail before the Tempo call, not send a blank
    // worker and let Tempo reject it.
    [Fact]
    public async Task DeliverConfirmedAsync_fails_as_tempo_when_no_worker_can_be_determined()
    {
        var clients = new RecordingIntegrationClientFactory();
        var service = new ConfirmedTaskDeliveryService(
            clients,
            new FixedSettingsStore(new UserSettings { TogglWorkspaceId = 42, JiraUser = "" }),
            new InMemoryAttemptRepository());

        var attempt = await service.DeliverConfirmedAsync(Item());

        Assert.Equal(DeliveryAttemptStatus.Failed, attempt.Status);
        Assert.Equal(DeliveryFailureCode.TempoFailed, attempt.FailureCode);
        Assert.Equal(0, clients.TempoWorklogRequests);
    }

    private static PlannedWorkItem Item() =>
        PlannedWorkItem.Create(new DateOnly(2026, 8, 13), "Planning", "CGM-1", "Reviewed work",
            TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT", start: new TimeOnly(9, 0));

    [Fact]
    public async Task DeliverConfirmedAsync_does_not_create_clients_until_invoked()
    {
        var clients = new RecordingIntegrationClientFactory();
        var service = new ConfirmedTaskDeliveryService(
            clients,
            new FixedSettingsStore(new UserSettings { TogglWorkspaceId = 42, JiraUser = "planner" }),
            new InMemoryAttemptRepository());

        Assert.Empty(clients.CreatedClients);

        await service.DeliverConfirmedAsync(PlannedWorkItem.Create(new DateOnly(2026, 8, 13), "Planning", "CGM-1", "Reviewed work", TimeSpan.FromMinutes(30), start: new TimeOnly(9, 0)));

        Assert.Equal(3, clients.CreatedClients.Count);
    }

    [Fact]
    public async Task DeliverConfirmedAsync_uses_explicit_end_time_and_selected_toggl_project()
    {
        var item = PlannedWorkItem.Create(
            new DateOnly(2026, 8, 13), "Planning", "CGM-1", "Reviewed work", TimeSpan.FromMinutes(30), "GDK", "DEVELOPMENT",
            start: new TimeOnly(9, 0), end: new TimeOnly(9, 30));
        item = item with { TogglProjectId = 77, PostToToggl = true };
        var clients = new RecordingIntegrationClientFactory();
        var service = new ConfirmedTaskDeliveryService(
            clients,
            new FixedSettingsStore(new UserSettings { TogglWorkspaceId = 42, JiraUser = "planner" }),
            new InMemoryAttemptRepository());

        await service.DeliverConfirmedAsync(item);

        Assert.Equal(77, clients.LastTogglRequest?.ProjectId);
        Assert.Equal(new TimeOnly(9, 30), TimeOnly.FromDateTime(clients.LastTogglRequest!.Stop.LocalDateTime));
    }

    [Fact]
    public async Task DeliverConfirmedAsync_rolls_an_overnight_end_time_to_the_next_day()
    {
        var item = PlannedWorkItem.Create(
            new DateOnly(2026, 8, 13), "On-call", "CGM-1", "Overnight incident", TimeSpan.FromMinutes(45), "GDK", "DEVELOPMENT",
            start: new TimeOnly(23, 30), end: new TimeOnly(0, 15));
        var clients = new RecordingIntegrationClientFactory();
        var service = new ConfirmedTaskDeliveryService(
            clients,
            new FixedSettingsStore(new UserSettings { TogglWorkspaceId = 42, JiraUser = "planner" }),
            new InMemoryAttemptRepository());

        var attempt = await service.DeliverConfirmedAsync(item);

        Assert.Equal(DeliveryAttemptStatus.Succeeded, attempt.Status);
        var request = clients.LastTogglRequest!;
        Assert.True(request.Stop > request.Start);
        Assert.Equal(new DateOnly(2026, 8, 14), DateOnly.FromDateTime(request.Stop.LocalDateTime));
        Assert.Equal(TimeSpan.FromMinutes(45), request.Stop - request.Start);
    }

    [Theory]
    [InlineData("toggl", DeliveryFailureCode.TogglFailed)]
    [InlineData("jira", DeliveryFailureCode.JiraFailed)]
    [InlineData("tempo", DeliveryFailureCode.TempoFailed)]
    public async Task DeliverConfirmedAsync_AttributesAClientSetupFailureToTheClientThatFailed(string failing, DeliveryFailureCode expected)
    {
        var item = PlannedWorkItem.Create(new DateOnly(2026, 9, 1), "Work", "CGM-1", "Comment",
            TimeSpan.FromMinutes(30), "CGM", "DEVELOPMENT", start: new TimeOnly(9, 0), end: new TimeOnly(9, 30));
        var service = new ConfirmedTaskDeliveryService(
            new FailingIntegrationClientFactory(failing),
            new FixedSettingsStore(new UserSettings { TogglWorkspaceId = 42, JiraUser = "planner" }),
            new InMemoryAttemptRepository());

        var attempt = await service.DeliverConfirmedAsync(item);

        Assert.Equal(DeliveryAttemptStatus.Failed, attempt.Status);
        Assert.Equal(expected, attempt.FailureCode);
    }

    [Fact]
    public async Task DeliverConfirmedAsync_ConvertsACancellationRaisedInsideDeliveryToCancelled()
    {
        // A cancellation that surfaces anywhere inside the `using` block -- not just from the three
        // client-creation calls Task 3 gave their own try/catch -- must still come back as a
        // Cancelled attempt rather than escaping DeliverConfirmedAsync unhandled. Disposing the
        // Toggl client after a fully successful delivery is a convenient, real IDisposable-shaped
        // place to raise that cancellation from inside the block.
        var item = PlannedWorkItem.Create(new DateOnly(2026, 9, 1), "Work", "CGM-1", "Comment",
            TimeSpan.FromMinutes(30), "CGM", "DEVELOPMENT", start: new TimeOnly(9, 0), end: new TimeOnly(9, 30));
        using var cts = new CancellationTokenSource();
        var service = new ConfirmedTaskDeliveryService(
            new CancelOnDisposeIntegrationClientFactory(cts),
            new FixedSettingsStore(new UserSettings { TogglWorkspaceId = 42, JiraUser = "planner" }),
            new StatelessAttemptRepository());

        var attempt = await service.DeliverConfirmedAsync(item, cts.Token);

        Assert.Equal(DeliveryAttemptStatus.Cancelled, attempt.Status);
        Assert.Equal(DeliveryFailureCode.Cancelled, attempt.FailureCode);
    }

    // Wraps a real (successful) Toggl client but cancels the shared token and raises
    // OperationCanceledException from Dispose(), simulating a cancellation raised inside the
    // `using` block after the client-creation try/catches have already succeeded.
    private sealed class CancelOnDisposeTogglClient(ITogglClient inner, CancellationTokenSource cancelOnDispose) : ITogglClient
    {
        public Task<IReadOnlyList<TogglTimeEntry>> GetTimeEntriesAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default) =>
            inner.GetTimeEntriesAsync(startDate, endDate, cancellationToken);
        public Task<IReadOnlyList<TogglProject>> GetProjectsAsync(long workspaceId, CancellationToken cancellationToken = default) =>
            inner.GetProjectsAsync(workspaceId, cancellationToken);
        public Task<TogglTimeEntry> CreateTimeEntryAsync(TogglCreateTimeEntryRequest request, CancellationToken cancellationToken = default) =>
            inner.CreateTimeEntryAsync(request, cancellationToken);
        public void Dispose()
        {
            inner.Dispose();
            cancelOnDispose.Cancel();
            throw new OperationCanceledException(cancelOnDispose.Token);
        }
    }

    private sealed class CancelOnDisposeIntegrationClientFactory(CancellationTokenSource cancelOnDispose) : IIntegrationClientFactory
    {
        private readonly RecordingIntegrationClientFactory inner = new();

        public async Task<ITogglClient> CreateTogglAsync(CancellationToken cancellationToken = default) =>
            new CancelOnDisposeTogglClient(await inner.CreateTogglAsync(cancellationToken), cancelOnDispose);
        public Task<JiraClient> CreateJiraAsync(CancellationToken cancellationToken = default) => inner.CreateJiraAsync(cancellationToken);
        public Task<TempoClient> CreateTempoAsync(CancellationToken cancellationToken = default) => inner.CreateTempoAsync(cancellationToken);
    }

    // Never actually persists -- isolates the assertion to what RecordSetupFailureAsync builds when
    // a cancellation is caught, regardless of whatever PostAllCoordinator already saved for the item.
    private sealed class StatelessAttemptRepository : IDeliveryAttemptRepository
    {
        public Task<DeliveryAttempt?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<DeliveryAttempt?>(null);
        public Task<IReadOnlyList<DeliveryAttempt>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DeliveryAttempt>>([]);
        public Task<DeliveryAttemptClaim> ClaimAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DeliveryAttemptClaim(new DeliveryAttempt(id, null, null, DeliveryAttemptStatus.InProgress, null, SlackDeliveryState.NotSupported), true));
        public Task SaveAsync(DeliveryAttempt attempt, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    // Fails exactly one client construction so the resulting failure code identifies which one.
    // CreateJiraAsync only fails for the "jira" case: for "tempo" it must succeed with a real
    // JiraClient (a fake can't stand in for the sealed type), so CreateTempoAsync is the one that fails.
    private sealed class FailingIntegrationClientFactory(string failing) : IIntegrationClientFactory
    {
        public Task<ITogglClient> CreateTogglAsync(CancellationToken cancellationToken = default) =>
            failing == "toggl"
                ? throw new InvalidOperationException("Toggl configuration is not configured.")
                : Task.FromResult<ITogglClient>(new UnusedTogglClient());

        public Task<JiraClient> CreateJiraAsync(CancellationToken cancellationToken = default) =>
            failing == "jira"
                ? throw new InvalidOperationException("Jira configuration is not configured.")
                : Task.FromResult(new JiraClient(
                    new HttpClient(new NeverCalledHttpMessageHandler()) { BaseAddress = new Uri("https://jira.example.test") },
                    new JiraOptions { BaseUrl = "https://jira.example.test", PersonalAccessToken = "unit-token" },
                    new IssueKeyValidator(new IssueKeyValidationOptions())));

        public Task<TempoClient> CreateTempoAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Tempo configuration is not configured.");
    }

    // Delivery never reaches this client's members because a later construction fails first.
    private sealed class UnusedTogglClient : ITogglClient
    {
        public Task<IReadOnlyList<TogglTimeEntry>> GetTimeEntriesAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<TogglProject>> GetProjectsAsync(long workspaceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<TogglTimeEntry> CreateTimeEntryAsync(TogglCreateTimeEntryRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public void Dispose() { }
    }

    private sealed class NeverCalledHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("HTTP should not be called.");
    }

    private sealed class FixedSettingsStore(UserSettings settings) : IUserSettingsStore
    {
        public UserSettings Load() => settings;
        public void Save(UserSettings value) { }
    }

    private sealed class RecordingIntegrationClientFactory : IIntegrationClientFactory
    {
        private readonly RecordingHandler handler = new();

        public List<TrackingHttpClient> CreatedClients { get; } = [];
        public int TogglRequests => handler.TogglRequests;
        public int JiraIssueRequests => handler.JiraIssueRequests;
        public int TempoWorklogRequests => handler.TempoWorklogRequests;
        public int JiraMyselfRequests => handler.JiraMyselfRequests;
        public string? LastTempoWorker => handler.LastTempoWorker;
        public RecordingHandler Handler => handler;
        public TogglCreateTimeEntryRequest? LastTogglRequest => handler.LastTogglRequest;

        public Task<ITogglClient> CreateTogglAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ITogglClient>(new TogglClient(CreateClient(), new TogglOptions { ApiToken = "unit-token" }));

        public Task<JiraClient> CreateJiraAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new JiraClient(CreateClient(), new JiraOptions { BaseUrl = "https://jira.example.test", PersonalAccessToken = "unit-token" }, new IssueKeyValidator(new IssueKeyValidationOptions())));

        public Task<TempoClient> CreateTempoAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new TempoClient(CreateClient(), new TempoOptions { BaseUrl = "https://jira.example.test", PersonalAccessToken = "unit-token" }));

        private TrackingHttpClient CreateClient()
        {
            var client = new TrackingHttpClient(handler);
            CreatedClients.Add(client);
            return client;
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int TogglRequests { get; private set; }
        public int JiraIssueRequests { get; private set; }
        public int TempoWorklogRequests { get; private set; }
        public int JiraMyselfRequests { get; private set; }
        public TogglCreateTimeEntryRequest? LastTogglRequest { get; private set; }
        public string? LastTempoWorker { get; private set; }
        public string? MyselfKey { get; set; }
        public string? MyselfName { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.Contains("time_entries", StringComparison.Ordinal))
            {
                TogglRequests++;
                var payload = request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken).GetAwaiter().GetResult();
                LastTogglRequest = new TogglCreateTimeEntryRequest(
                    payload.GetProperty("workspace_id").GetInt64(),
                    payload.GetProperty("description").GetString()!,
                    payload.GetProperty("start").GetDateTimeOffset(),
                    payload.GetProperty("stop").GetDateTimeOffset(),
                    payload.TryGetProperty("project_id", out var projectId) ? projectId.GetInt64() : null);
                return Json(new { id = 101L });
            }
            if (request.RequestUri.AbsolutePath.Contains("myself", StringComparison.Ordinal))
            {
                JiraMyselfRequests++;
                return Json(new { name = MyselfName, displayName = "Planner", emailAddress = "planner@example.test", key = MyselfKey });
            }
            if (request.RequestUri.AbsolutePath.Contains("issue", StringComparison.Ordinal))
            {
                JiraIssueRequests++;
                return Json(new { id = "201", key = "CGM-1", fields = new { summary = "Planning" } });
            }

            TempoWorklogRequests++;
            var tempoPayload = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
            LastTempoWorker = tempoPayload.TryGetProperty("worker", out var worker) ? worker.GetString() : null;
            return Json(new { tempoWorklogId = 301L, worker = "planner", originTaskId = "201", started = "2026-08-13T09:00:00", timeSpentSeconds = 1800, comment = "Reviewed work" });
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private sealed class TrackingHttpClient(HttpMessageHandler handler) : HttpClient(handler, false)
    {
        public bool WasDisposed { get; private set; }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }

    private sealed class InMemoryAttemptRepository : IDeliveryAttemptRepository
    {
        private readonly Dictionary<Guid, DeliveryAttempt> attempts = [];
        public Task<DeliveryAttempt?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(attempts.GetValueOrDefault(id));
        public Task<IReadOnlyList<DeliveryAttempt>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DeliveryAttempt>>(attempts.Values.ToArray());
        public Task<DeliveryAttemptClaim> ClaimAsync(Guid id, CancellationToken cancellationToken = default)
        {
            if (attempts.TryGetValue(id, out var existing)) return Task.FromResult(new DeliveryAttemptClaim(existing, false));
            var created = new DeliveryAttempt(id, null, null, DeliveryAttemptStatus.InProgress, null, SlackDeliveryState.NotSupported);
            attempts[id] = created;
            return Task.FromResult(new DeliveryAttemptClaim(created, true));
        }
        public Task SaveAsync(DeliveryAttempt attempt, CancellationToken cancellationToken = default) { attempts[attempt.PlannedWorkItemId] = attempt; return Task.CompletedTask; }
    }
}
