using System.Net;
using System.Net.Http.Headers;
using System.Text;
using GDK.TimeSync.Toggl;

namespace GDK.TimeSync.Tests;

public sealed class TogglClientTests
{
    [Fact]
    public async Task GetTimeEntriesAsync_reads_start_stop_duration_and_description()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""[{"id":1,"description":"CGM | CGMFRAVII-2767 | Knowledge Transfer","start":"2026-08-07T08:15:00-04:00","stop":"2026-08-07T08:45:00-04:00","duration":1800}]"""));
        using var httpClient = CreateHttpClient(handler);
        using var client = CreateClient(httpClient);

        var result = await client.GetTimeEntriesAsync(new DateOnly(2026, 8, 7), new DateOnly(2026, 8, 7));

        var entry = Assert.Single(result);
        Assert.Equal("CGM | CGMFRAVII-2767 | Knowledge Transfer", entry.Description);
        Assert.Equal(new DateTimeOffset(2026, 8, 7, 8, 15, 0, TimeSpan.FromHours(-4)), entry.Start);
        Assert.Equal(new DateTimeOffset(2026, 8, 7, 8, 45, 0, TimeSpan.FromHours(-4)), entry.Stop);
        Assert.Equal(1800, entry.DurationSeconds);
        Assert.Equal("/api/v9/me/time_entries", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal("Basic", handler.LastRequest.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task GetTimeEntriesAsync_widens_the_toggl_query_by_one_day_since_a_same_day_range_returns_nothing()
    {
        var date = new DateOnly(2026, 8, 25);
        var handler = new StubHttpMessageHandler(_ => TimeEntriesResponse((1, "Today", ToOffset(date, new TimeOnly(8, 0)), ToOffset(date, new TimeOnly(8, 30)))));
        using var httpClient = CreateHttpClient(handler);
        using var client = CreateClient(httpClient);

        var result = await client.GetTimeEntriesAsync(date, date);

        Assert.Single(result);
        Assert.Equal("start_date=2026-08-25&end_date=2026-08-26", handler.LastRequest!.RequestUri!.Query.TrimStart('?'));
    }

    [Fact]
    public async Task GetTimeEntriesAsync_excludes_entries_outside_the_requested_local_date_range()
    {
        var date = new DateOnly(2026, 8, 25);
        var handler = new StubHttpMessageHandler(_ => TimeEntriesResponse(
            (1, "In range", ToOffset(date, new TimeOnly(8, 0)), ToOffset(date, new TimeOnly(8, 30))),
            (2, "Widened-in day", ToOffset(date.AddDays(1), new TimeOnly(0, 15)), ToOffset(date.AddDays(1), new TimeOnly(0, 30)))));
        using var httpClient = CreateHttpClient(handler);
        using var client = CreateClient(httpClient);

        var result = await client.GetTimeEntriesAsync(date, date);

        var entry = Assert.Single(result);
        Assert.Equal(1, entry.Id);
    }

    [Fact]
    public async Task CreateTimeEntryAsync_posts_a_typed_entry_to_its_workspace()
    {
        string? body = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse("""{"id":7,"description":"Knowledge Transfer","start":"2026-08-07T08:15:00-04:00","stop":"2026-08-07T08:45:00-04:00","duration":1800}""");
        });
        using var httpClient = CreateHttpClient(handler);
        using var client = CreateClient(httpClient);
        var request = new TogglCreateTimeEntryRequest(42, "Knowledge Transfer", new DateTimeOffset(2026, 8, 7, 8, 15, 0, TimeSpan.FromHours(-4)), new DateTimeOffset(2026, 8, 7, 8, 45, 0, TimeSpan.FromHours(-4)), 314);

        var result = await client.CreateTimeEntryAsync(request);

        Assert.Equal(7, result.Id);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/api/v9/workspaces/42/time_entries", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"description\":\"Knowledge Transfer\"", body);
        Assert.Contains("\"workspace_id\":42", body);
        Assert.Contains("\"project_id\":314", body);
        Assert.Contains("\"duration\":1800", body);
    }

    [Fact]
    public async Task CreateTimeEntryAsync_includes_the_created_with_field_toggl_v9_requires()
    {
        string? body = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse("""{"id":7,"description":"Knowledge Transfer","start":"2026-08-07T08:15:00-04:00","stop":"2026-08-07T08:45:00-04:00","duration":1800}""");
        });
        using var httpClient = CreateHttpClient(handler);
        using var client = CreateClient(httpClient);

        await client.CreateTimeEntryAsync(ValidRequest());

        Assert.Contains("\"created_with\":\"GDK.TimeSync\"", body);
    }

    [Fact]
    public async Task GetProjectsAsync_reads_projects_from_the_selected_workspace()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""[{"id":314,"name":"GDK"}]"""));
        using var httpClient = CreateHttpClient(handler);
        using var client = CreateClient(httpClient);

        var project = Assert.Single(await client.GetProjectsAsync(42));

        Assert.Equal(new TogglProject(314, "GDK"), project);
        Assert.Equal("/api/v9/workspaces/42/projects", handler.LastRequest!.RequestUri!.AbsolutePath);
    }

    // A large shared workspace returns projects the user cannot see the name of; they arrived with
    // a blank name and filled the project picker with unlabelled, unpickable rows.
    [Fact]
    public async Task GetProjectsAsync_drops_projects_that_came_back_without_a_usable_name()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(
            """[{"id":1,"name":""},{"id":314,"name":"GDK"},{"id":2,"name":null},{"id":3,"name":"   "},{"id":4,"name":"CGM"}]"""));
        using var httpClient = CreateHttpClient(handler);
        using var client = CreateClient(httpClient);

        var projects = await client.GetProjectsAsync(42);

        Assert.Equal([new TogglProject(314, "GDK"), new TogglProject(4, "CGM")], projects);
    }

    [Fact]
    public async Task CreateTimeEntryAsync_rejects_invalid_entries_without_an_http_request()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("{}"));
        using var httpClient = CreateHttpClient(handler);
        using var client = CreateClient(httpClient);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.CreateTimeEntryAsync(new TogglCreateTimeEntryRequest(0, "Description", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1))));

        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task CreateTimeEntryAsync_returns_a_safe_exception_for_an_unsuccessful_response()
    {
        const string secret = "test-token";
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var httpClient = CreateHttpClient(handler);
        using var client = new TogglClient(httpClient, new TogglOptions { ApiToken = secret });

        var exception = await Assert.ThrowsAsync<TogglApiException>(() => client.CreateTimeEntryAsync(ValidRequest()));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateTimeEntryAsync_returns_a_safe_exception_for_malformed_json()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("{"));
        using var httpClient = CreateHttpClient(handler);
        using var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<TogglApiException>(() => client.CreateTimeEntryAsync(ValidRequest()));

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.DoesNotContain("test-token", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateTimeEntryAsync_wraps_transport_errors_without_disclosing_authorization()
    {
        const string secret = "test-token";
        var handler = new ThrowingHttpMessageHandler(new HttpRequestException($"request failed for {secret}"));
        using var httpClient = CreateHttpClient(handler);
        using var client = new TogglClient(httpClient, new TogglOptions { ApiToken = secret });

        var exception = await Assert.ThrowsAsync<TogglApiException>(() => client.CreateTimeEntryAsync(ValidRequest()));

        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateTimeEntryAsync_propagates_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var handler = new CancellingHttpMessageHandler();
        using var httpClient = CreateHttpClient(handler);
        using var client = CreateClient(httpClient);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CreateTimeEntryAsync(ValidRequest(), cancellation.Token));

        Assert.True(handler.LastCancellationToken.IsCancellationRequested);
    }

    private static TogglClient CreateClient(HttpClient httpClient) =>
        new(httpClient, new TogglOptions { ApiToken = "test-token" });

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://api.track.toggl.com/api/v9/") };

    private static TogglCreateTimeEntryRequest ValidRequest() =>
        new(42, "Knowledge Transfer", new DateTimeOffset(2026, 8, 7, 8, 15, 0, TimeSpan.FromHours(-4)), new DateTimeOffset(2026, 8, 7, 8, 45, 0, TimeSpan.FromHours(-4)));

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage TimeEntriesResponse(params (long Id, string Description, DateTimeOffset Start, DateTimeOffset Stop)[] entries)
    {
        var items = entries.Select(e =>
            $"{{\"id\":{e.Id},\"description\":\"{e.Description}\",\"start\":\"{e.Start:O}\",\"stop\":\"{e.Stop:O}\",\"duration\":{(long)(e.Stop - e.Start).TotalSeconds}}}");
        return JsonResponse("[" + string.Join(",", items) + "]");
    }

    private static DateTimeOffset ToOffset(DateOnly date, TimeOnly time) =>
        new(date.ToDateTime(time), TimeZoneInfo.Local.GetUtcOffset(date.ToDateTime(time)));

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class ThrowingHttpMessageHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class CancellingHttpMessageHandler : HttpMessageHandler
    {
        public CancellationToken LastCancellationToken { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
        }
    }
}
