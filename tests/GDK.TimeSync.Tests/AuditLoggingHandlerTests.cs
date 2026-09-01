using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.Services;

namespace GDK.TimeSync.Tests;

public sealed class AuditLoggingHandlerTests
{
    [Fact]
    public async Task Logs_info_without_a_body_for_a_successful_call()
    {
        var log = new RecordingAuditLog();
        using var client = CreateClient(log, "GDK.TimeSync.Tempo", HttpStatusCode.OK, "{\"id\":55}");

        await client.GetAsync("rest/tempo-timesheets/4/worklogs/55");

        var entry = Assert.Single(log.Entries);
        Assert.Equal(AuditLevel.Info, entry.Level);
        Assert.Equal("GDK.TimeSync.Tempo", entry.Category);
        Assert.Contains("GET /rest/tempo-timesheets/4/worklogs/55 -> 200 OK", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("\"id\":55", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logs_error_with_the_response_body_for_a_failed_call()
    {
        var log = new RecordingAuditLog();
        const string body = "{\"errors\":[{\"message\":\"Worker could not be found\"}]}";
        using var client = CreateClient(log, "GDK.TimeSync.Tempo", HttpStatusCode.BadRequest, body);

        await client.PostAsync("rest/tempo-timesheets/4/worklogs", new StringContent("{}"));

        var entry = Assert.Single(log.Entries);
        Assert.Equal(AuditLevel.Error, entry.Level);
        Assert.Contains("-> 400 BadRequest", entry.Message, StringComparison.Ordinal);
        Assert.Contains("Worker could not be found", entry.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Never_writes_the_authorization_header(HttpStatusCode status)
    {
        var log = new RecordingAuditLog();
        using var client = CreateClient(log, "GDK.TimeSync.Jira", status, "{}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", "c3VwZXItc2VjcmV0LXRva2Vu");

        await client.GetAsync("rest/api/2/myself");

        var entry = Assert.Single(log.Entries);
        Assert.DoesNotContain("c3VwZXItc2VjcmV0LXRva2Vu", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Basic", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Never_writes_any_part_of_the_slack_webhook_url()
    {
        var log = new RecordingAuditLog();
        const string secretPath = "T0A1B2C3/9z8y7x6w5v";
        var handler = new AuditLoggingHandler(log, "GDK.TimeSync.Slack", redactUri: true)
        {
            InnerHandler = new StubHandler(HttpStatusCode.OK, "ok")
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri($"https://hooks.slack.com/triggers/{secretPath}") };

        await client.PostAsync("", new StringContent("{}"));

        var entry = Assert.Single(log.Entries);
        Assert.Contains("POST <slack webhook> -> 200 OK", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secretPath, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("hooks.slack.com", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("triggers", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logs_the_status_even_when_the_response_body_cannot_be_read()
    {
        var log = new RecordingAuditLog();
        var handler = new AuditLoggingHandler(log, "GDK.TimeSync.Toggl")
        {
            InnerHandler = new ThrowingBodyHandler(HttpStatusCode.InternalServerError)
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.track.toggl.com/api/v9/") };

        await client.GetAsync("me/time_entries");

        var entry = Assert.Single(log.Entries);
        Assert.Equal(AuditLevel.Error, entry.Level);
        Assert.Contains("-> 500 InternalServerError", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logs_a_transport_failure_as_an_error_and_rethrows()
    {
        var log = new RecordingAuditLog();
        var handler = new AuditLoggingHandler(log, "GDK.TimeSync.Toggl")
        {
            InnerHandler = new FaultingHandler()
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.track.toggl.com/api/v9/") };

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("me/time_entries"));

        var entry = Assert.Single(log.Entries);
        Assert.Equal(AuditLevel.Error, entry.Level);
        Assert.Contains("transport failure", entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient CreateClient(IAuditLog log, string clientName, HttpStatusCode status, string body)
    {
        var handler = new AuditLoggingHandler(log, clientName) { InnerHandler = new StubHandler(status, body) };
        return new HttpClient(handler) { BaseAddress = new Uri("https://jira.example.test/") };
    }

    private sealed record Entry(AuditLevel Level, string Category, string Message);

    private sealed class RecordingAuditLog : IAuditLog
    {
        public List<Entry> Entries { get; } = [];
        public void Write(AuditLevel level, string category, string message) => Entries.Add(new Entry(level, category, message));
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    private sealed class ThrowingBodyHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new FaultingContent() });
    }

    private sealed class FaultingContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new IOException("unreadable");
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    private sealed class FaultingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("no route to host");
    }
}
