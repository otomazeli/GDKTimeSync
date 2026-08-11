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
        Assert.Equal("start_date=2026-08-07&end_date=2026-08-07", handler.LastRequest.RequestUri.Query.TrimStart('?'));
        Assert.Equal("Basic", handler.LastRequest.Headers.Authorization!.Scheme);
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
        var request = new TogglCreateTimeEntryRequest(42, "Knowledge Transfer", new DateTimeOffset(2026, 8, 7, 8, 15, 0, TimeSpan.FromHours(-4)), new DateTimeOffset(2026, 8, 7, 8, 45, 0, TimeSpan.FromHours(-4)));

        var result = await client.CreateTimeEntryAsync(request);

        Assert.Equal(7, result.Id);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/api/v9/workspaces/42/time_entries", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"description\":\"Knowledge Transfer\"", body);
        Assert.Contains("\"workspace_id\":42", body);
        Assert.Contains("\"duration\":1800", body);
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
