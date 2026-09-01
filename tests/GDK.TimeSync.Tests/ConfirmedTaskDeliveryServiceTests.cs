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
        public TogglCreateTimeEntryRequest? LastTogglRequest { get; private set; }

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
            if (request.RequestUri.AbsolutePath.Contains("issue", StringComparison.Ordinal))
            {
                JiraIssueRequests++;
                return Json(new { id = "201", key = "CGM-1", fields = new { summary = "Planning" } });
            }

            TempoWorklogRequests++;
            await request.Content!.ReadFromJsonAsync<object>(cancellationToken);
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
