using System.Net;
using System.Net.Http.Headers;
using System.Text;
using GDK.TimeSync.Tempo;

namespace GDK.TimeSync.Tests;

public sealed class TempoClientTests
{
    [Fact]
    public async Task GetWorkAttributesAsync_discovers_attributes_with_bearer_authentication()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""[{"id":1,"name":"Account"}]"""));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        var result = await client.GetWorkAttributesAsync();

        Assert.Equal("Account", result[0].GetProperty("name").GetString());
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("/rest/tempo-core/1/work-attribute", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "test-pat"), handler.LastRequest.Headers.Authorization);
    }

    [Fact]
    public async Task CreateWorklogAsync_posts_a_tempo_worklog()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""{"tempoWorklogId":1}"""));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);
        var request = new TempoWorklogCreateRequest("odimar", "12345", new DateTime(2026, 8, 7, 8, 15, 0), 1_800, "Knowledge Transfer");

        await client.CreateWorklogAsync(request);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/rest/tempo-timesheets/4/worklogs", handler.LastRequest.RequestUri!.AbsolutePath);
        var body = await handler.LastRequest.Content!.ReadAsStringAsync();
        Assert.Contains("\"originTaskId\":\"12345\"", body);
        Assert.Contains("\"timeSpentSeconds\":1800", body);
        Assert.Contains("\"started\":\"2026-08-07T08:15:00.000\"", body);
    }

    private static TempoClient CreateClient(HttpClient httpClient) =>
        new(httpClient, new TempoOptions { BaseUrl = "https://jira.cgm.ag", PersonalAccessToken = "test-pat" });

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://jira.cgm.ag/") };

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
