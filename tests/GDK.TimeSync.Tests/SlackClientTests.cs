using System.Net;
using System.Text;
using System.Text.Json;
using GDK.TimeSync.Slack;

namespace GDK.TimeSync.Tests;

public sealed class SlackClientTests
{
    [Fact]
    public async Task PostAsync_SendsTheWorkflowBuilderDataVariables()
    {
        HttpRequestMessage? request = null;
        using var client = CreateClient(new StubHttpMessageHandler(message =>
        {
            request = message;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        await client.PostAsync(new SlackDailyUpdate(new DateOnly(2026, 8, 13), "Daily update", "Completed tasks", "GDK | CGM-1 Work | *Done*", "planner"));

        Assert.Equal(HttpMethod.Post, request!.Method);
        var raw = await request.Content!.ReadAsStringAsync();
        var body = JsonDocument.Parse(raw).RootElement;
        Assert.Equal("Daily update", body.GetProperty("SlackTitle").GetString());
        Assert.Equal("Completed tasks", body.GetProperty("SlackTaskHeading").GetString());
        Assert.Equal("GDK | CGM-1 Work | *Done*", body.GetProperty("SlackExtraLines").GetString());
        Assert.Equal("planner", body.GetProperty("SlackUser").GetString());
        Assert.Equal("", body.GetProperty("TogglProject").GetString());
        Assert.Equal("", body.GetProperty("JiraIssueKey").GetString());
        Assert.Equal("", body.GetProperty("Description").GetString());
        Assert.Equal("", body.GetProperty("Status").GetString());
    }

    [Fact]
    public async Task PostAsync_TreatsAnySuccessStatusAsSuccessRegardlessOfResponseBody()
    {
        using var client = CreateClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json")
        }));

        await client.PostAsync(SampleUpdate());
    }

    [Fact]
    public async Task PostAsync_WebhookFailure_DoesNotExposeWebhook()
    {
        const string webhook = "https://hooks.slack.com/services/private";
        using var client = CreateClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)), webhook);

        var exception = await Assert.ThrowsAsync<SlackApiException>(() => client.PostAsync(SampleUpdate()));

        Assert.Equal(SlackFailureCode.UnsuccessfulResponse, exception.FailureCode);
        Assert.DoesNotContain("hooks.slack.com", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostAsync_TransportFailure_DoesNotExposeUnderlyingMessage()
    {
        const string secret = "https://hooks.slack.com/services/private";
        using var client = CreateClient(new ThrowingHttpMessageHandler(new HttpRequestException(secret)));

        var exception = await Assert.ThrowsAsync<SlackApiException>(() => client.PostAsync(SampleUpdate()));

        Assert.Equal(SlackFailureCode.Transport, exception.FailureCode);
        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostAsync_OperationalFailure_DoesNotExposeHandlerSecret()
    {
        const string secret = "handler-secret-must-not-leak";
        using var client = CreateClient(new ThrowingHttpMessageHandler(new InvalidOperationException(secret)));

        var exception = await Assert.ThrowsAsync<SlackApiException>(() => client.PostAsync(SampleUpdate()));

        Assert.Equal(SlackFailureCode.Transport, exception.FailureCode);
        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostAsync_InjectedSlackException_IsSanitized()
    {
        const string sentinel = "https://hooks.slack.com/services/sentinel {\"text\":\"payload-sentinel\"}";
        var injected = new SlackApiException(sentinel, SlackFailureCode.InvalidResponse);
        using var client = CreateClient(new ThrowingHttpMessageHandler(injected));

        var exception = await Assert.ThrowsAsync<SlackApiException>(() => client.PostAsync(SampleUpdate()));

        Assert.NotSame(injected, exception);
        Assert.Equal(SlackFailureCode.Transport, exception.FailureCode);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(sentinel, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostAsync_Cancellation_IsSafeFailure()
    {
        using var client = CreateClient(new CancellingHttpMessageHandler());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAsync<SlackApiException>(() => client.PostAsync(SampleUpdate(), cancellation.Token));

        Assert.Equal(SlackFailureCode.Cancelled, exception.FailureCode);
    }

    private static SlackDailyUpdate SampleUpdate() =>
        new(new DateOnly(2026, 8, 13), "Daily update", "Completed tasks", "GDK | CGM-1 Work | *Done*", "planner");

    private static SlackClient CreateClient(HttpMessageHandler handler, string baseAddress = "https://slack.invalid/") =>
        new(new HttpClient(handler) { BaseAddress = new Uri(baseAddress) });

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
