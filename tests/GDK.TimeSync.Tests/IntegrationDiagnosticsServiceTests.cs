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
    // Issue #13: TempoClient hardcodes the work-category attribute id, copied from a reference client
    // that captured it from Tempo's UI. The diagnostic already fetches the attribute list and threw it
    // away -- naming it here is how you check that id on a corporate machine that has the executable
    // and no SDK, debugger or network access to anything else.
    [Fact]
    public async Task RunAsync_names_the_tempo_work_attributes_it_found()
    {
        var clients = new RecordingIntegrationClientFactory(
            workAttributes: [new TempoAttribute(4, "Work-Category"), new TempoAttribute(1, "Account")]);
        var service = new IntegrationDiagnosticsService(clients);

        var results = await service.RunAsync();

        var tempo = results.Single(result => result.Target == IntegrationDiagnosticTarget.Tempo);
        Assert.True(tempo.IsSuccessful);
        Assert.Equal("Available: work category sent as attribute id 4; instance has Work-Category (id 4), Account (id 1)", tempo.SafeMessage);
    }

    // A red row reading only "Unavailable" could not separate a rejected token from a host that does
    // not resolve, so the reason had to be dug out of the log to learn which one it was.
    [Fact]
    public async Task RunAsync_names_the_status_code_a_target_refused_with()
    {
        var clients = new RecordingIntegrationClientFactory(
            refusingTarget: IntegrationDiagnosticTarget.Jira, refusalStatus: HttpStatusCode.Unauthorized);
        var service = new IntegrationDiagnosticsService(clients);

        var results = await service.RunAsync();

        var jira = results.Single(result => result.Target == IntegrationDiagnosticTarget.Jira);
        Assert.False(jira.IsSuccessful);
        Assert.Equal("Unavailable: 401 Unauthorized", jira.SafeMessage);
    }

    // How a timeout arrives -- and what the page showed on the first real run of this change: the
    // client wraps the transport failure with no status, and its constant message is the only thing
    // that says which of the two it was.
    [Fact]
    public async Task RunAsync_falls_back_to_the_clients_own_message_when_there_is_no_status_code()
    {
        const string secret = "diagnostic-secret-sentinel";
        var clients = new RecordingIntegrationClientFactory(failingTarget: IntegrationDiagnosticTarget.Tempo, failureDetail: secret);
        var service = new IntegrationDiagnosticsService(clients);

        var results = await service.RunAsync();

        var tempo = results.Single(result => result.Target == IntegrationDiagnosticTarget.Tempo);
        Assert.Equal("Unavailable: Unable to reach Tempo.", tempo.SafeMessage);
        // The transport exception's own detail stays out: only the client's constant is shown.
        Assert.DoesNotContain(secret, tempo.SafeMessage, StringComparison.Ordinal);
    }

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
                // Names the identity Tempo is sent as a worklog `worker`, so a wrong one shows up on
                // the Diagnostics page rather than as a 400 at delivery time.
                new IntegrationDiagnosticResult(IntegrationDiagnosticTarget.Jira, true, "Available: name=planner"),
                new IntegrationDiagnosticResult(IntegrationDiagnosticTarget.Tempo, true, "Available: work category sent as attribute id 4; instance reports no work attributes")
            ],
            results);
        Assert.Equal(
            [
                $"Toggl GET /me/time_entries?start_date={today:yyyy-MM-dd}&end_date={today.AddDays(1):yyyy-MM-dd}",
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
                new IntegrationDiagnosticResult(IntegrationDiagnosticTarget.Jira, false, "Unavailable: InvalidOperationException"),
                new IntegrationDiagnosticResult(IntegrationDiagnosticTarget.Tempo, true, "Available: work category sent as attribute id 4; instance reports no work attributes")
            ],
            results);
        Assert.Equal(["Toggl GET /me/time_entries?start_date=" + DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd") + "&end_date=" + DateOnly.FromDateTime(DateTime.Today).AddDays(1).ToString("yyyy-MM-dd"), "Tempo GET /rest/tempo-core/1/work-attribute"], clients.Requests);
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
                new IntegrationDiagnosticResult(IntegrationDiagnosticTarget.Tempo, true, "Available: work category sent as attribute id 4; instance reports no work attributes")
            ],
            results);
        Assert.Equal(
            [
                "Toggl GET /me/time_entries?start_date=" + DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd") + "&end_date=" + DateOnly.FromDateTime(DateTime.Today).AddDays(1).ToString("yyyy-MM-dd"),
                "Jira GET /rest/api/2/myself",
                "Tempo GET /rest/tempo-core/1/work-attribute"
            ],
            clients.Requests);
        Assert.All(clients.CreatedClients, client => Assert.True(client.WasDisposed));
    }

    [Fact]
    public async Task RunAsync_contains_client_disposal_failures_and_continues_to_later_targets()
    {
        var clients = new RecordingIntegrationClientFactory(throwingDisposeTarget: IntegrationDiagnosticTarget.Jira);
        var service = new IntegrationDiagnosticsService(clients);

        var results = await service.RunAsync();

        Assert.Equal(
            [
                new IntegrationDiagnosticResult(IntegrationDiagnosticTarget.Toggl, true, "Available"),
                new IntegrationDiagnosticResult(IntegrationDiagnosticTarget.Jira, false, "Unavailable"),
                new IntegrationDiagnosticResult(IntegrationDiagnosticTarget.Tempo, true, "Available: work category sent as attribute id 4; instance reports no work attributes")
            ],
            results);
        Assert.Equal(
            [
                "Toggl GET /me/time_entries?start_date=" + DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd") + "&end_date=" + DateOnly.FromDateTime(DateTime.Today).AddDays(1).ToString("yyyy-MM-dd"),
                "Jira GET /rest/api/2/myself",
                "Tempo GET /rest/tempo-core/1/work-attribute"
            ],
            clients.Requests);
        Assert.All(clients.CreatedClients, client => Assert.True(client.WasDisposed));
    }

    private sealed class RecordingIntegrationClientFactory(
        IntegrationDiagnosticTarget? failingTarget = null,
        IntegrationDiagnosticTarget? cancellingTarget = null,
        string? failureDetail = null,
        IntegrationDiagnosticTarget? throwingDisposeTarget = null,
        TempoAttribute[]? workAttributes = null,
        IntegrationDiagnosticTarget? refusingTarget = null,
        HttpStatusCode refusalStatus = HttpStatusCode.Unauthorized) : IIntegrationClientFactory
    {
        private readonly RecordingHandler handler = new(failingTarget, cancellingTarget, failureDetail, workAttributes, refusingTarget, refusalStatus);

        public List<TrackingHttpClient> CreatedClients { get; } = [];
        public IReadOnlyList<string> Requests => handler.Requests;

        public Task<ITogglClient> CreateTogglAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ITogglClient>(new TogglClient(CreateClient(IntegrationDiagnosticTarget.Toggl), new TogglOptions { BaseUrl = "https://integrations.example.test/", ApiToken = "unit-token" }));

        public Task<JiraClient> CreateJiraAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfFactoryFails(IntegrationDiagnosticTarget.Jira);
            return Task.FromResult(new JiraClient(CreateClient(IntegrationDiagnosticTarget.Jira), new JiraOptions { BaseUrl = "https://integrations.example.test", PersonalAccessToken = "unit-token" }, new IssueKeyValidator(new IssueKeyValidationOptions())));
        }

        public Task<TempoClient> CreateTempoAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new TempoClient(CreateClient(IntegrationDiagnosticTarget.Tempo), new TempoOptions { BaseUrl = "https://integrations.example.test", PersonalAccessToken = "unit-token" }));

        private TrackingHttpClient CreateClient(IntegrationDiagnosticTarget target)
        {
            var client = new TrackingHttpClient(handler, throwingDisposeTarget == target);
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
        string? failureDetail,
        TempoAttribute[]? workAttributes = null,
        IntegrationDiagnosticTarget? refusingTarget = null,
        HttpStatusCode refusalStatus = HttpStatusCode.Unauthorized) : HttpMessageHandler
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
            if (refusingTarget == target)
                return Task.FromResult(new HttpResponseMessage(refusalStatus) { Content = new StringContent("") });

            return Task.FromResult(target switch
            {
                IntegrationDiagnosticTarget.Toggl => Json(Array.Empty<TogglTimeEntry>()),
                IntegrationDiagnosticTarget.Jira => Json(new { name = "planner", displayName = "Planner", emailAddress = "planner@example.test" }),
                IntegrationDiagnosticTarget.Tempo => Json(workAttributes ?? []),
                _ => throw new ArgumentOutOfRangeException()
            });
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private sealed class TrackingHttpClient(HttpMessageHandler handler, bool throwOnDispose = false) : HttpClient(handler, disposeHandler: false)
    {
        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
            if (throwOnDispose) throw new InvalidOperationException("Test disposal failure.");
        }
    }
}
