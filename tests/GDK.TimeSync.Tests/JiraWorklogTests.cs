using System.Net;
using System.Text;
using System.Text.Json;
using GDK.TimeSync.Core;
using GDK.TimeSync.Jira;

namespace GDK.TimeSync.Tests;

// Jira's own worklog endpoint as an alternative to Tempo's. It has no `worker` field -- the author is
// whoever owns the PAT -- so the "User is invalid" rejection that blocked every Tempo post cannot
// happen here. It also adjusts the remaining estimate itself, which the Tempo path has to compute by
// hand. Not yet wired into delivery: whether Tempo picks these up, and what happens to the work
// category, has to be confirmed against the real instance first.
public sealed class JiraWorklogTests
{
    [Fact]
    public async Task PostsToTheIssueWorklogEndpointAndLetsJiraAdjustTheEstimate()
    {
        var handler = new StubHandler(_ => JsonResponse("""{"id":"90210","timeSpentSeconds":1800}"""));
        using var client = CreateClient(handler);

        await client.CreateWorklogAsync("CGMFRAVII-8431",
            new DateTimeOffset(2026, 9, 3, 9, 0, 0, TimeSpan.FromHours(2)), 1800, "Reviewed work");

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/rest/api/2/issue/CGMFRAVII-8431/worklog", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal("?adjustEstimate=auto", handler.LastRequest.RequestUri.Query);
    }

    [Fact]
    public async Task ReturnsTheWorklogIdJiraAssigned()
    {
        var handler = new StubHandler(_ => JsonResponse("""{"id":"90210","timeSpentSeconds":1800}"""));
        using var client = CreateClient(handler);

        var worklog = await client.CreateWorklogAsync("CGMFRAVII-8431",
            new DateTimeOffset(2026, 9, 3, 9, 0, 0, TimeSpan.FromHours(2)), 1800, "Reviewed work");

        Assert.Equal("90210", worklog.Id);
        Assert.Equal(1800, worklog.TimeSpentSeconds);
    }

    // Jira rejects an offset written the ISO way, with a colon. It wants +0200, not +02:00 -- and the
    // offset has to be there at all, unlike Tempo, which takes a naive local time.
    [Fact]
    public async Task SendsTheStartedTimestampInJirasOffsetFormat()
    {
        string? body = null;
        var handler = new StubHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse("""{"id":"90210","timeSpentSeconds":1800}""");
        });
        using var client = CreateClient(handler);

        await client.CreateWorklogAsync("CGMFRAVII-8431",
            new DateTimeOffset(2026, 9, 3, 9, 0, 0, TimeSpan.FromHours(2)), 1800, "Reviewed work");

        using var payload = JsonDocument.Parse(body!);
        Assert.Equal("2026-09-03T09:00:00.000+0200", payload.RootElement.GetProperty("started").GetString());
        Assert.Equal(1800, payload.RootElement.GetProperty("timeSpentSeconds").GetInt32());
        Assert.Equal("Reviewed work", payload.RootElement.GetProperty("comment").GetString());
    }

    // No worker, no author, no originTaskId: the whole class of identity failure Tempo produced
    // cannot arise here, and a stray field would be the only way to reintroduce it.
    [Fact]
    public async Task SendsNoIdentityFieldAtAllBecauseThePatOwnerIsTheAuthor()
    {
        string? body = null;
        var handler = new StubHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse("""{"id":"90210","timeSpentSeconds":1800}""");
        });
        using var client = CreateClient(handler);

        await client.CreateWorklogAsync("CGMFRAVII-8431",
            new DateTimeOffset(2026, 9, 3, 9, 0, 0, TimeSpan.FromHours(2)), 1800, "Reviewed work");

        using var payload = JsonDocument.Parse(body!);
        foreach (var identityField in new[] { "worker", "author", "updateAuthor", "originTaskId" })
            Assert.False(payload.RootElement.TryGetProperty(identityField, out _), identityField);
    }

    [Fact]
    public async Task RejectsAnInvalidIssueKeyBeforeCallingJira()
    {
        var handler = new StubHandler(_ => JsonResponse("{}"));
        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<FormatException>(() => client.CreateWorklogAsync("not a key",
            DateTimeOffset.UtcNow, 1800, "Reviewed work"));
        Assert.Null(handler.LastRequest);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RejectsANonPositiveDuration(int timeSpentSeconds)
    {
        var handler = new StubHandler(_ => JsonResponse("{}"));
        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.CreateWorklogAsync("CGMFRAVII-8431",
            DateTimeOffset.UtcNow, timeSpentSeconds, "Reviewed work"));
        Assert.Null(handler.LastRequest);
    }

    // Carries the status so a caller can tell a refusal from a timeout -- the distinction delivery
    // uses to decide whether an attempt is safe to repeat.
    [Fact]
    public async Task SurfacesAJiraRefusalWithItsStatusCode()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<JiraApiException>(() => client.CreateWorklogAsync("CGMFRAVII-8431",
            DateTimeOffset.UtcNow, 1800, "Reviewed work"));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task ReportsAnUnreachableJiraWithoutAStatusCode()
    {
        var handler = new ThrowingHandler(new HttpRequestException("unreachable"));
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<JiraApiException>(() => client.CreateWorklogAsync("CGMFRAVII-8431",
            DateTimeOffset.UtcNow, 1800, "Reviewed work"));

        Assert.Null(exception.StatusCode);
    }

    private static JiraClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://jira.example.test/") },
            new JiraOptions { BaseUrl = "https://jira.example.test", PersonalAccessToken = "unit-token" },
            new IssueKeyValidator(new IssueKeyValidationOptions()));

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw exception;
    }
}
