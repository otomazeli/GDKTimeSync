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

    public void Dispose() => httpClient.Dispose();

    private async Task<T> GetAsync<T>(string requestUri, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(requestUri, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new JiraApiException("Unable to reach Jira.", innerException: exception);
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
            catch (JsonException exception)
            {
                throw new JiraApiException("Jira returned an invalid response.", response.StatusCode, exception);
            }
        }
    }
}
