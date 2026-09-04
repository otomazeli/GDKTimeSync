using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using GDK.TimeSync.Core;

namespace GDK.TimeSync.Jira;

public sealed class JiraClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly IssueKeyValidator issueKeyValidator;

    public JiraClient(HttpClient httpClient, JiraOptions options, IssueKeyValidator issueKeyValidator)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(issueKeyValidator);

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new ArgumentException("Jira:BaseUrl must be an absolute URL.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.PersonalAccessToken))
        {
            throw new ArgumentException("Jira personal access token is required.", nameof(options));
        }

        this.httpClient = httpClient;
        this.httpClient.BaseAddress ??= new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/");
        this.httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.PersonalAccessToken);
        this.issueKeyValidator = issueKeyValidator;
    }

    public Task<JiraCurrentUser> GetMyselfAsync(CancellationToken cancellationToken = default) =>
        GetAsync<JiraCurrentUser>("rest/api/2/myself", cancellationToken);

    public Task<JiraIssue> GetIssueAsync(string issueKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issueKey);

        if (!issueKeyValidator.IsValid(issueKey))
        {
            throw new FormatException("The Jira issue key is invalid.");
        }

        return GetAsync<JiraIssue>($"rest/api/2/issue/{Uri.EscapeDataString(issueKey)}", cancellationToken);
    }

    /// <summary>
    /// Logs work through Jira rather than Tempo. Jira takes the author from the token, so the
    /// "worker" rejection that blocks the Tempo path cannot arise, and adjustEstimate=auto has Jira
    /// decrement the remaining estimate itself instead of the caller fetching and computing it.
    /// </summary>
    /// <remarks>
    /// The work category has no equivalent here: it is a Tempo work attribute, stored by Tempo and
    /// not by Jira, so a worklog created this way carries no category.
    /// </remarks>
    public async Task<JiraWorklog> CreateWorklogAsync(
        string issueKey,
        DateTimeOffset started,
        int timeSpentSeconds,
        string comment,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issueKey);
        if (!issueKeyValidator.IsValid(issueKey))
            throw new FormatException("The Jira issue key is invalid.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeSpentSeconds);

        var payload = new
        {
            started = FormatStarted(started),
            timeSpentSeconds,
            comment
        };

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync(
                $"rest/api/2/issue/{Uri.EscapeDataString(issueKey)}/worklog?adjustEstimate=auto",
                payload, JsonOptions, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            // No status: Jira never answered, so whether the worklog exists is unknown.
            throw new JiraApiException("Unable to reach Jira.", null, exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new JiraApiException("Jira returned an unsuccessful response.", response.StatusCode);

            try
            {
                return await response.Content.ReadFromJsonAsync<JiraWorklog>(JsonOptions, cancellationToken)
                    ?? throw new JiraApiException("Jira returned an empty response.", response.StatusCode);
            }
            catch (JsonException exception)
            {
                throw new JiraApiException("Jira returned an invalid response.", response.StatusCode, exception);
            }
        }
    }

    // Jira wants the offset without a colon -- 2026-09-03T09:00:00.000+0200, not +02:00 -- and
    // rejects the ISO form that "zzz" produces. Tempo, by contrast, takes a naive local time.
    private static string FormatStarted(DateTimeOffset started)
    {
        var offset = started.Offset;
        var sign = offset < TimeSpan.Zero ? '-' : '+';
        return string.Create(CultureInfo.InvariantCulture,
            $"{started:yyyy-MM-dd'T'HH:mm:ss.fff}{sign}{Math.Abs(offset.Hours):D2}{Math.Abs(offset.Minutes):D2}");
    }

    public void Dispose() => httpClient.Dispose();

    private async Task<T> GetAsync<T>(string requestUri, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(requestUri, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new JiraApiException("Unable to reach Jira.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new JiraApiException("Jira returned an unsuccessful response.", response.StatusCode);
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                    ?? throw new JiraApiException("Jira returned an empty response.", response.StatusCode);
            }
            catch (JsonException)
            {
                throw new JiraApiException("Jira returned an invalid response.", response.StatusCode);
            }
        }
    }
}
