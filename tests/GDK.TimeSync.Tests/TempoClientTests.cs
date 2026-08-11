using System.Net;
using System.Net.Http.Headers;
using System.Text;
using GDK.TimeSync.Tempo;

namespace GDK.TimeSync.Tests;

public sealed class TempoClientTests
{
    [Fact]
    public async Task GetWorkAttributesAsync_returns_typed_attributes_with_bearer_authentication()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""[{"id":1,"name":"Account"}]"""));
        using var httpClient = CreateHttpClient(handler);
        using ITempoClient client = CreateClient(httpClient);

        var result = await client.GetWorkAttributesAsync();

        var attribute = Assert.Single(result);
        Assert.Equal(1, attribute.Id);
        Assert.Equal("Account", attribute.Name);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("/rest/tempo-core/1/work-attribute", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "test-pat"), handler.LastRequest.Headers.Authorization);
    }

    [Fact]
    public async Task GetExistingWorklogsAsync_scopes_the_request_to_the_jira_issue()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""[{"tempoWorklogId":5,"worker":"odimar","originTaskId":"12345","started":"2026-08-07T08:15:00.000","timeSpentSeconds":1800,"comment":"Knowledge Transfer"}]"""));
        using var httpClient = CreateHttpClient(handler);
        using ITempoClient client = CreateClient(httpClient);

        var result = await client.GetExistingWorklogsAsync("12345");

        var worklog = Assert.Single(result);
        Assert.Equal(5, worklog.TempoWorklogId);
        Assert.Equal("Knowledge Transfer", worklog.Comment);
        Assert.Equal("/rest/tempo-timesheets/4/worklogs", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal("originTaskId=12345", handler.LastRequest.RequestUri.Query.TrimStart('?'));
    }

    [Fact]
    public async Task GetExistingWorklogsAsync_escapes_the_issue_identifier_query_value()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("[]"));
        using var httpClient = CreateHttpClient(handler);
        using ITempoClient client = CreateClient(httpClient);

        await client.GetExistingWorklogsAsync("issue id/?");

        Assert.Equal("originTaskId=issue%20id%2F%3F", handler.LastRequest!.RequestUri!.Query.TrimStart('?'));
    }

    [Fact]
    public async Task GetExistingWorklogsAsync_returns_a_safe_exception_for_an_unsuccessful_response()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        using var httpClient = CreateHttpClient(handler);
        using ITempoClient client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<TempoApiException>(() => client.GetExistingWorklogsAsync("12345"));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
    }

    [Fact]
    public async Task GetExistingWorklogsAsync_returns_a_safe_exception_for_malformed_json()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("{"));
        using var httpClient = CreateHttpClient(handler);
        using ITempoClient client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<TempoApiException>(() => client.GetExistingWorklogsAsync("12345"));

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
    }

    [Fact]
    public async Task GetExistingWorklogsAsync_wraps_transport_errors_and_propagates_cancellation()
    {
        var transportHandler = new ThrowingHttpMessageHandler(new HttpRequestException("unreachable"));
        using var transportHttpClient = CreateHttpClient(transportHandler);
        using ITempoClient transportClient = CreateClient(transportHttpClient);

        await Assert.ThrowsAsync<TempoApiException>(() => transportClient.GetExistingWorklogsAsync("12345"));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancellingHandler = new CancellingHttpMessageHandler();
        using var cancellingHttpClient = CreateHttpClient(cancellingHandler);
        using ITempoClient cancellingClient = CreateClient(cancellingHttpClient);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancellingClient.GetExistingWorklogsAsync("12345", cancellation.Token));

        Assert.True(cancellingHandler.LastCancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task CreateWorklogAsync_posts_a_typed_tempo_worklog()
    {
        string? body = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse("""{"tempoWorklogId":1,"worker":"odimar","originTaskId":"12345","started":"2026-08-07T08:15:00.000","timeSpentSeconds":1800,"comment":"Knowledge Transfer"}""");
        });
        using var httpClient = CreateHttpClient(handler);
        using ITempoClient client = CreateClient(httpClient);

        var result = await client.CreateWorklogAsync(ValidRequest());

        Assert.Equal(1, result.TempoWorklogId);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/rest/tempo-timesheets/4/worklogs", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"originTaskId\":\"12345\"", body);
        Assert.Contains("\"timeSpentSeconds\":1800", body);
        Assert.Contains("\"started\":\"2026-08-07T08:15:00.000\"", body);
    }

    [Fact]
    public async Task GetWorklogAsync_returns_null_when_the_worklog_is_missing()
    {
        var content = new TrackingContent("{}");
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = content });
        using var httpClient = CreateHttpClient(handler);
        using ITempoClient client = CreateClient(httpClient);

        var result = await client.GetWorklogAsync(9);

        Assert.Null(result);
        Assert.Equal("/rest/tempo-timesheets/4/worklogs/9", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.True(content.WasDisposed);
    }

    [Fact]
    public async Task GetWorklogAsync_throws_for_other_unsuccessful_responses_and_disposes_them()
    {
        var content = new TrackingContent("{}");
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = content });
        using var httpClient = CreateHttpClient(handler);
        using ITempoClient client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<TempoApiException>(() => client.GetWorklogAsync(9));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.True(content.WasDisposed);
    }

    [Fact]
    public async Task GetWorklogAsync_returns_safe_errors_for_malformed_json_transport_and_cancellation()
    {
        var malformedHandler = new StubHttpMessageHandler(_ => JsonResponse("{"));
        using var malformedHttpClient = CreateHttpClient(malformedHandler);
        using ITempoClient malformedClient = CreateClient(malformedHttpClient);
        var malformed = await Assert.ThrowsAsync<TempoApiException>(() => malformedClient.GetWorklogAsync(9));
        Assert.Equal(HttpStatusCode.OK, malformed.StatusCode);

        var transportHandler = new ThrowingHttpMessageHandler(new HttpRequestException("unreachable"));
        using var transportHttpClient = CreateHttpClient(transportHandler);
        using ITempoClient transportClient = CreateClient(transportHttpClient);
        await Assert.ThrowsAsync<TempoApiException>(() => transportClient.GetWorklogAsync(9));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancellingHandler = new CancellingHttpMessageHandler();
        using var cancellingHttpClient = CreateHttpClient(cancellingHandler);
        using ITempoClient cancellingClient = CreateClient(cancellingHttpClient);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancellingClient.GetWorklogAsync(9, cancellation.Token));
        Assert.True(cancellingHandler.LastCancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task UpdateWorklogAsync_puts_the_typed_tempo_worklog()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""{"tempoWorklogId":9,"worker":"odimar","originTaskId":"12345","started":"2026-08-07T08:15:00.000","timeSpentSeconds":900,"comment":"Updated"}"""));
        using var httpClient = CreateHttpClient(handler);
        using ITempoClient client = CreateClient(httpClient);

        var result = await client.UpdateWorklogAsync(9, ValidRequest() with { TimeSpentSeconds = 900, Comment = "Updated" });

        Assert.Equal(900, result.TimeSpentSeconds);
        Assert.Equal(HttpMethod.Put, handler.LastRequest!.Method);
        Assert.Equal("/rest/tempo-timesheets/4/worklogs/9", handler.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task UpdateWorklogAsync_returns_a_safe_exception_for_an_unsuccessful_response()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict));
        using var httpClient = CreateHttpClient(handler);
        using ITempoClient client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<TempoApiException>(() => client.UpdateWorklogAsync(9, ValidRequest()));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
    }

    [Fact]
    public async Task UpdateWorklogAsync_returns_a_safe_exception_for_malformed_json()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("{"));
        using var httpClient = CreateHttpClient(handler);
        using ITempoClient client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<TempoApiException>(() => client.UpdateWorklogAsync(9, ValidRequest()));

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
    }

    [Fact]
    public async Task UpdateWorklogAsync_wraps_transport_errors_and_propagates_cancellation()
    {
        var transportHandler = new ThrowingHttpMessageHandler(new HttpRequestException("unreachable"));
        using var transportHttpClient = CreateHttpClient(transportHandler);
        using ITempoClient transportClient = CreateClient(transportHttpClient);
        await Assert.ThrowsAsync<TempoApiException>(() => transportClient.UpdateWorklogAsync(9, ValidRequest()));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancellingHandler = new CancellingHttpMessageHandler();
        using var cancellingHttpClient = CreateHttpClient(cancellingHandler);
        using ITempoClient cancellingClient = CreateClient(cancellingHttpClient);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancellingClient.UpdateWorklogAsync(9, ValidRequest(), cancellation.Token));
        Assert.True(cancellingHandler.LastCancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task CreateWorklogAsync_rejects_malformed_input_without_an_http_request()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("{}"));
        using var httpClient = CreateHttpClient(handler);
        using ITempoClient client = CreateClient(httpClient);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.CreateWorklogAsync(ValidRequest() with { TimeSpentSeconds = 0 }));

        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task CreateWorklogAsync_returns_safe_exceptions_for_unsuccessful_and_malformed_responses()
    {
        const string secret = "test-pat";
        var unauthorizedHandler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var unauthorizedClient = CreateHttpClient(unauthorizedHandler);
        using ITempoClient client = new TempoClient(unauthorizedClient, new TempoOptions { BaseUrl = "https://jira.example.test", PersonalAccessToken = secret });

        var unsuccessful = await Assert.ThrowsAsync<TempoApiException>(() => client.CreateWorklogAsync(ValidRequest()));

        Assert.Equal(HttpStatusCode.Unauthorized, unsuccessful.StatusCode);
        Assert.DoesNotContain(secret, unsuccessful.ToString(), StringComparison.Ordinal);

        var malformedHandler = new StubHttpMessageHandler(_ => JsonResponse("{"));
        using var malformedClient = CreateHttpClient(malformedHandler);
        using ITempoClient malformed = CreateClient(malformedClient);

        var invalid = await Assert.ThrowsAsync<TempoApiException>(() => malformed.GetWorkAttributesAsync());

        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.DoesNotContain(secret, invalid.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateWorklogAsync_wraps_transport_errors_without_disclosing_authorization()
    {
        const string secret = "test-pat";
        var handler = new ThrowingHttpMessageHandler(new HttpRequestException($"request failed for {secret}"));
        using var httpClient = CreateHttpClient(handler);
        using ITempoClient client = new TempoClient(httpClient, new TempoOptions { BaseUrl = "https://jira.example.test", PersonalAccessToken = secret });

        var exception = await Assert.ThrowsAsync<TempoApiException>(() => client.CreateWorklogAsync(ValidRequest()));

        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateWorklogAsync_propagates_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var handler = new CancellingHttpMessageHandler();
        using var httpClient = CreateHttpClient(handler);
        using ITempoClient client = CreateClient(httpClient);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CreateWorklogAsync(ValidRequest(), cancellation.Token));

        Assert.True(handler.LastCancellationToken.IsCancellationRequested);
    }

    private static TempoClient CreateClient(HttpClient httpClient) =>
        new(httpClient, new TempoOptions { BaseUrl = "https://jira.example.test", PersonalAccessToken = "test-pat" });

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://jira.example.test/") };

    private static TempoWorklogRequest ValidRequest() =>
        new("odimar", "12345", new DateTime(2026, 8, 7, 8, 15, 0), 1_800, "Knowledge Transfer");

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

    private sealed class TrackingContent(string content) : StringContent(content, Encoding.UTF8, "application/json")
    {
        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}
