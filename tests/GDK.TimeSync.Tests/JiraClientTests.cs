using System.Net;
using System.Net.Http.Headers;
using System.Text;
using GDK.TimeSync.Core;
using GDK.TimeSync.Jira;

namespace GDK.TimeSync.Tests;

public sealed class JiraClientTests
{
    [Fact]
    public async Task GetMyselfAsync_sends_a_bearer_token_to_the_expected_endpoint()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""{"name":"odimar","displayName":"Odimar","emailAddress":"odimar@example.com"}"""));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        var result = await client.GetMyselfAsync();

        Assert.Equal("Odimar", result.DisplayName);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("/rest/api/2/myself", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "test-pat"), handler.LastRequest.Headers.Authorization);
    }

    [Fact]
    public async Task GetIssueAsync_gets_a_validated_issue_key()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""{"key":"CGMFRAVII-2767","fields":{"summary":"Knowledge Transfer"}}"""));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        var result = await client.GetIssueAsync("CGMFRAVII-2767");

        Assert.Equal("CGMFRAVII-2767", result.Key);
        Assert.Equal("Knowledge Transfer", result.Summary);
        Assert.Equal("/rest/api/2/issue/CGMFRAVII-2767", handler.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetIssueAsync_rejects_an_invalid_issue_key_without_an_http_request()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("{}"));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        await Assert.ThrowsAsync<FormatException>(() => client.GetIssueAsync("not-an-issue"));

        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task GetMyselfAsync_returns_a_safe_exception_for_an_unsuccessful_response()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<JiraApiException>(() => client.GetMyselfAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.DoesNotContain("test-pat", exception.Message);
    }

    private static JiraClient CreateClient(HttpClient httpClient) =>
        new(httpClient, new JiraOptions { BaseUrl = "https://jira.cgm.ag", PersonalAccessToken = "test-pat" }, new IssueKeyValidator(new IssueKeyValidationOptions()));

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
