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
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.track.toggl.com/api/v9/") };
        var client = new TogglClient(httpClient, new TogglOptions { ApiToken = "test-token" });

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
}
