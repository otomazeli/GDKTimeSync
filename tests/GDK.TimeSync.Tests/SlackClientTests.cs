using System.Net;
using System.Text;
using GDK.TimeSync.Slack;

namespace GDK.TimeSync.Tests;

public sealed class SlackClientTests
{
    [Fact]
    public async Task PostAsync_SendsOnlyTheTextPayload()
    {
        HttpRequestMessage? request = null;
        using var client = CreateClient(new StubHttpMessageHandler(message =>
        {
            request = message;
            return PlainTextResponse("ok");
        }));

        await client.PostAsync(new SlackDailyUpdate(new DateOnly(2026, 8, 13), "Daily update"));

        Assert.Equal(HttpMethod.Post, request!.Method);
        Assert.Equal("{\"text\":\"Daily update\"}", await request.Content!.ReadAsStringAsync());
    }

    [Fact]
    public async Task PostAsync_AcceptsTheExpectedSuccessResponse()
    {
        using var client = CreateClient(new StubHttpMessageHandler(_ => PlainTextResponse("ok")));

        await client.PostAsync(new SlackDailyUpdate(new DateOnly(2026, 8, 13), "Daily update"));
    }

    [Fact]
    public async Task PostAsync_WebhookFailure_DoesNotExposeWebhook()
    {
        const string webhook = "https://hooks.slack.com/services/private";
        using var client = CreateClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)), webhook);

        var exception = await Assert.ThrowsAsync<SlackApiException>(() => client.PostAsync(new SlackDailyUpdate(new DateOnly(2026, 8, 13), "Daily update")));

        Assert.Equal(SlackFailureCode.UnsuccessfulResponse, exception.FailureCode);
        Assert.DoesNotContain("hooks.slack.com", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostAsync_MalformedSuccessResponse_IsSafeFailure()
    {
        using var client = CreateClient(new StubHttpMessageHandler(_ => PlainTextResponse("unexpected response")));

        var exception = await Assert.ThrowsAsync<SlackApiException>(() => client.PostAsync(new SlackDailyUpdate(new DateOnly(2026, 8, 13), "Daily update")));

        Assert.Equal(SlackFailureCode.InvalidResponse, exception.FailureCode);
    }

    [Fact]
    public async Task PostAsync_TransportFailure_DoesNotExposeUnderlyingMessage()
    {
        const string secret = "https://hooks.slack.com/services/private";
        using var client = CreateClient(new ThrowingHttpMessageHandler(new HttpRequestException(secret)));

        var exception = await Assert.ThrowsAsync<SlackApiException>(() => client.PostAsync(new SlackDailyUpdate(new DateOnly(2026, 8, 13), "Daily update")));

        Assert.Equal(SlackFailureCode.Transport, exception.FailureCode);
        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostAsync_OperationalFailure_DoesNotExposeHandlerSecret()
    {
        const string secret = "handler-secret-must-not-leak";
        using var client = CreateClient(new ThrowingHttpMessageHandler(new InvalidOperationException(secret)));

        var exception = await Assert.ThrowsAsync<SlackApiException>(() => client.PostAsync(new SlackDailyUpdate(new DateOnly(2026, 8, 13), "Daily update")));

        Assert.Equal(SlackFailureCode.Transport, exception.FailureCode);
        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostAsync_PreservesSafeHandlerFailure()
    {
        using var client = CreateClient(new ThrowingHttpMessageHandler(new SlackApiException("Slack returned an invalid response.", SlackFailureCode.InvalidResponse)));

        var exception = await Assert.ThrowsAsync<SlackApiException>(() => client.PostAsync(new SlackDailyUpdate(new DateOnly(2026, 8, 13), "Daily update")));

        Assert.Equal(SlackFailureCode.InvalidResponse, exception.FailureCode);
    }

    [Fact]
    public async Task PostAsync_Cancellation_IsSafeFailure()
    {
        using var client = CreateClient(new CancellingHttpMessageHandler());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAsync<SlackApiException>(() => client.PostAsync(new SlackDailyUpdate(new DateOnly(2026, 8, 13), "Daily update"), cancellation.Token));

        Assert.Equal(SlackFailureCode.Cancelled, exception.FailureCode);
    }

    private static SlackClient CreateClient(HttpMessageHandler handler, string baseAddress = "https://slack.invalid/") =>
        new(new HttpClient(handler) { BaseAddress = new Uri(baseAddress) });

    private static HttpResponseMessage PlainTextResponse(string body) => new()
    {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(body, Encoding.UTF8, "text/plain")
    };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }

    private sealed class ThrowingHttpMessageHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class CancellingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromCanceled<HttpResponseMessage>(cancellationToken);
    }
}
