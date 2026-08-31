using System.Net;
using System.Text;
using GDK.TimeSync.Core;
using GDK.TimeSync.Jira;
using GDK.TimeSync.Tempo;
using Microsoft.Extensions.DependencyInjection;

namespace GDK.TimeSync.Tests;

public sealed class TempoConsoleCommandsTests
{
    [Fact]
    public async Task TempoCreate_resolves_the_jira_issue_id_before_posting_to_tempo()
    {
        var steps = new List<string>();
        string? tempoPayload = null;
        var issueKeyValidator = CreateIssueKeyValidator();
        var jiraHandler = new StubHttpMessageHandler(request =>
        {
            steps.Add(request.RequestUri!.AbsolutePath);
            return request.RequestUri.AbsolutePath.Contains("/issue/", StringComparison.Ordinal)
                ? JsonResponse("""{"id":"12345","key":"CGMFRAVII-2767","fields":{"summary":"Knowledge Transfer"}}""")
                : JsonResponse("""{"name":"odimar","displayName":"Odimar","emailAddress":"odimar@example.com"}""");
        });
        var tempoHandler = new StubHttpMessageHandler(request =>
        {
            steps.Add(request.RequestUri!.AbsolutePath);
            tempoPayload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse("""{"tempoWorklogId":1,"worker":"odimar","originTaskId":"12345","started":"2026-08-07T08:15:00.000","timeSpentSeconds":1800,"comment":"Knowledge Transfer"}""");
        });
        using var jiraHttpClient = new HttpClient(jiraHandler) { BaseAddress = new Uri("https://jira.example.test/") };
        using var tempoHttpClient = new HttpClient(tempoHandler) { BaseAddress = new Uri("https://jira.example.test/") };
        using var services = CreateServices(jiraHttpClient, tempoHttpClient, issueKeyValidator);

        await TempoConsoleCommands.RunAsync(["tempo-create", "CGMFRAVII-2767", "2026-08-07", "08:15", "1800", "Knowledge Transfer"], services);

        Assert.Equal(["/rest/api/2/issue/CGMFRAVII-2767", "/rest/api/2/myself", "/rest/tempo-timesheets/4/worklogs"], steps);
        Assert.Contains("\"originTaskId\":\"12345\"", tempoPayload);
        Assert.DoesNotContain("CGMFRAVII-2767", tempoPayload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TempoCreate_rejects_an_invalid_jira_key_without_any_http_request()
    {
        var issueKeyValidator = CreateIssueKeyValidator();
        var jiraHandler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("No Jira request expected."));
        var tempoHandler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("No Tempo request expected."));
        using var jiraHttpClient = new HttpClient(jiraHandler) { BaseAddress = new Uri("https://jira.example.test/") };
        using var tempoHttpClient = new HttpClient(tempoHandler) { BaseAddress = new Uri("https://jira.example.test/") };
        using var services = CreateServices(jiraHttpClient, tempoHttpClient, issueKeyValidator);

        await Assert.ThrowsAsync<FormatException>(() => TempoConsoleCommands.RunAsync(["tempo-create", "not-an-issue", "2026-08-07", "08:15", "1800", "Knowledge Transfer"], services));

        Assert.Null(jiraHandler.LastRequest);
        Assert.Null(tempoHandler.LastRequest);
    }

    [Fact]
    public async Task TempoCreate_does_not_post_when_jira_returns_no_issue_id()
    {
        var issueKeyValidator = CreateIssueKeyValidator();
        var jiraHandler = new StubHttpMessageHandler(_ => JsonResponse("""{"key":"CGMFRAVII-2767","fields":{"summary":"Knowledge Transfer"}}"""));
        var tempoHandler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("No Tempo request expected."));
        using var jiraHttpClient = new HttpClient(jiraHandler) { BaseAddress = new Uri("https://jira.example.test/") };
        using var tempoHttpClient = new HttpClient(tempoHandler) { BaseAddress = new Uri("https://jira.example.test/") };
        using var services = CreateServices(jiraHttpClient, tempoHttpClient, issueKeyValidator);

        await Assert.ThrowsAsync<InvalidOperationException>(() => TempoConsoleCommands.RunAsync(["tempo-create", "CGMFRAVII-2767", "2026-08-07", "08:15", "1800", "Knowledge Transfer"], services));

        Assert.NotNull(jiraHandler.LastRequest);
        Assert.Null(tempoHandler.LastRequest);
    }

    private static ServiceProvider CreateServices(HttpClient jiraHttpClient, HttpClient tempoHttpClient, IssueKeyValidator issueKeyValidator) =>
        new ServiceCollection()
            .AddSingleton(issueKeyValidator)
            .AddSingleton(new JiraClient(jiraHttpClient, new JiraOptions { BaseUrl = "https://jira.example.test", PersonalAccessToken = "test-pat" }, issueKeyValidator))
            .AddSingleton(new TempoClient(tempoHttpClient, new TempoOptions { BaseUrl = "https://jira.example.test", PersonalAccessToken = "test-pat" }))
            .BuildServiceProvider();

    private static IssueKeyValidator CreateIssueKeyValidator() =>
        new(new IssueKeyValidationOptions());

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
