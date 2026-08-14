using System.Net;
using System.Net.Http.Json;
using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Jira;
using GDK.TimeSync.Tempo;
using GDK.TimeSync.Toggl;

namespace GDK.TimeSync.Tests;

public sealed class IntegrationDiagnosticsServiceTests
{
    [Fact]
    public async Task RunAsync_checks_all_read_only_targets_in_order_for_today_and_disposes_created_clients()
    {
        var clients = new RecordingIntegrationClientFactory();
        var service = new IntegrationDiagnosticsService(clients);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var results = await service.RunAsync();

        Assert.Equal(
            [
                new IntegrationDiagnosticResult(IntegrationDiagnosticTarget.Toggl, true, "Available"),
                new IntegrationDiagnosticResult(IntegrationDiagnosticTarget.Jira, true, "Available"),
                new IntegrationDiagnosticResult(IntegrationDiagnosticTarget.Tempo, true, "Available")
            ],
            results);
        Assert.Equal(
            [
                $"Toggl GET /me/time_entries?start_date={today:yyyy-MM-dd}&end_date={today:yyyy-MM-dd}",
                "Jira GET /rest/api/2/myself",
                "Tempo GET /rest/tempo-core/1/work-attribute"
            ],
            clients.Requests);
        Assert.All(clients.CreatedClients, client => Assert.True(client.WasDisposed));
        Assert.All(clients.Requests, request => Assert.Contains(" GET ", request, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_continues_after_an_unavailable_target_without_exposing_failure_details()
    {
        const string secret = "diagnostic-secret-sentinel";
        var clients = new RecordingIntegrationClientFactory(failingTarget: IntegrationDiagnosticTarget.Jira, failureDetail: secret);
        var service = new IntegrationDiagnosticsService(clients);

        var results = await service.RunAsync();

        Assert.Equal(
            [
                new IntegrationDiagnosticResult(IntegrationDiagnosticTarget.Toggl, true, "Available"),
                new IntegrationDiagnosticResult(IntegrationDiagnosticTarget.Jira, false, "Unavailable"),
                new IntegrationDiagnosticResult(IntegrationDiagnosticTarget.Tempo, true, "Available")
            ],
            results);
        Assert.Equal(["Toggl GET /me/time_entries?start_date=" + DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd") + "&end_date=" + DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd"), "Tempo GET /rest/tempo-core/1/work-attribute"], clients.Requests);
        Assert.DoesNotContain(results, result => result.SafeMessage.Contains(secret, StringComparison.Ordinal));
        Assert.All(clients.CreatedClients, client => Assert.True(client.WasDisposed));
    }

    [Fact]
    public async Task RunAsync_reports_cancellation_as_a_safe_category_and_still_checks_remaining_targets()
    {
        var clients = new RecordingIntegrationClientFactory(cancellingTarget: IntegrationDiagnosticTarget.Jira);
        var service = new IntegrationDiagnosticsService(clients);

        var results = await service.RunAsync();

        Assert.Equal(
            [
                new IntegrationDiagnosticResult(IntegrationDiagnosticTarget.Toggl, true, "Available"),
                new IntegrationDiagnosticResult(IntegrationDiagnosticTarget.Jira, false, "Cancelled"),
                new IntegrationDiagnosticResult(IntegrationDiagnosticTarget.Tempo, true, "Available")
            ],
            results);
        Assert.Equal(
            [
                "Toggl GET /me/time_entries?start_date=" + DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd") + "&end_date=" + DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd"),
                "Jira GET /rest/api/2/myself",
                "Tempo GET /rest/tempo-core/1/work-attribute"
            ],
            clients.Requests);
        Assert.All(clients.CreatedClients, client => Assert.True(client.WasDisposed));
    }

    private sealed class RecordingIntegrationClientFactory(
        IntegrationDiagnosticTarget? failingTarget = null,
        IntegrationDiagnosticTarget? cancellingTarget = null,
        string? failureDetail = null) : IIntegrationClientFactory
    {
        private readonly RecordingHandler handler = new(failingTarget, cancellingTarget, failureDetail);

        public List<TrackingHttpClient> CreatedClients { get; } = [];
        public IReadOnlyList<string> Requests => handler.Requests;

        public Task<ITogglClient> CreateTogglAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ITogglClient>(new TogglClient(CreateClient(), new TogglOptions { BaseUrl = "https://integrations.example.test/", ApiToken = "unit-token" }));

        public Task<JiraClient> CreateJiraAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfFactoryFails(IntegrationDiagnosticTarget.Jira);
            return Task.FromResult(new JiraClient(CreateClient(), new JiraOptions { BaseUrl = "https://integrations.example.test", PersonalAccessToken = "unit-token" }, new IssueKeyValidator(new IssueKeyValidationOptions())));
        }

        public Task<TempoClient> CreateTempoAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new TempoClient(CreateClient(), new TempoOptions { BaseUrl = "https://integrations.example.test", PersonalAccessToken = "unit-token" }));

        private TrackingHttpClient CreateClient()
        {
            var client = new TrackingHttpClient(handler);
            CreatedClients.Add(client);
            return client;
        }

        private void ThrowIfFactoryFails(IntegrationDiagnosticTarget target)
        {
            if (failingTarget == target)
                throw new InvalidOperationException(failureDetail);
        }
    }

    private sealed class RecordingHandler(
        IntegrationDiagnosticTarget? failingTarget,
        IntegrationDiagnosticTarget? cancellingTarget,
        string? failureDetail) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var target = request.RequestUri!.AbsolutePath switch
            {
                "/me/time_entries" => IntegrationDiagnosticTarget.Toggl,
                "/rest/api/2/myself" => IntegrationDiagnosticTarget.Jira,
                "/rest/tempo-core/1/work-attribute" => IntegrationDiagnosticTarget.Tempo,
                _ => throw new Xunit.Sdk.XunitException($"Unexpected request: {request.Method} {request.RequestUri}")
            };
            Requests.Add($"{target} {request.Method} {request.RequestUri.PathAndQuery}");

            if (cancellingTarget == target)
                throw new OperationCanceledException();
            if (failingTarget == target)
                throw new HttpRequestException(failureDetail);

            return Task.FromResult(target switch
            {
                IntegrationDiagnosticTarget.Toggl => Json(Array.Empty<TogglTimeEntry>()),
                IntegrationDiagnosticTarget.Jira => Json(new { name = "planner", displayName = "Planner", emailAddress = "planner@example.test" }),
                IntegrationDiagnosticTarget.Tempo => Json(Array.Empty<TempoAttribute>()),
                _ => throw new ArgumentOutOfRangeException()
            });
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private sealed class TrackingHttpClient(HttpMessageHandler handler) : HttpClient(handler, disposeHandler: false)
    {
        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}
