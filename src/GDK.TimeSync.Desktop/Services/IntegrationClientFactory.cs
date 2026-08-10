using System.Net.Http;
using GDK.TimeSync.Core;
using GDK.TimeSync.Jira;
using GDK.TimeSync.Tempo;
using GDK.TimeSync.Toggl;

namespace GDK.TimeSync.Desktop.Services;

public sealed class IntegrationClientFactory(
    ICredentialStore credentials,
    IUserSettingsStore settings,
    IHttpClientFactory httpClientFactory,
    IssueKeyValidator issueKeyValidator) : IIntegrationClientFactory
{
    public const string TogglHttpClientName = "GDK.TimeSync.Toggl";
    public const string JiraHttpClientName = "GDK.TimeSync.Jira";
    public const string TempoHttpClientName = "GDK.TimeSync.Tempo";

    public async Task<ITogglClient> CreateTogglAsync(CancellationToken cancellationToken = default)
    {
        var apiToken = await GetRequiredCredentialAsync(CredentialKeys.TogglApiToken, "Toggl", cancellationToken);
        return new TogglClient(httpClientFactory.CreateClient(TogglHttpClientName), new TogglOptions { ApiToken = apiToken });
    }

    public async Task<JiraClient> CreateJiraAsync(CancellationToken cancellationToken = default)
    {
        var baseUrl = GetJiraBaseUrl();
        var personalAccessToken = await GetRequiredCredentialAsync(CredentialKeys.JiraPat, "Jira", cancellationToken);
        return new JiraClient(httpClientFactory.CreateClient(JiraHttpClientName), new JiraOptions { BaseUrl = baseUrl, PersonalAccessToken = personalAccessToken }, issueKeyValidator);
    }

    public async Task<TempoClient> CreateTempoAsync(CancellationToken cancellationToken = default)
    {
        var baseUrl = GetJiraBaseUrl();
        var personalAccessToken = await GetRequiredCredentialAsync(CredentialKeys.JiraPat, "Tempo", cancellationToken);
        return new TempoClient(httpClientFactory.CreateClient(TempoHttpClientName), new TempoOptions { BaseUrl = baseUrl, PersonalAccessToken = personalAccessToken });
    }

    private string GetJiraBaseUrl()
    {
        var baseUrl = settings.Load().JiraBaseUrl;
        return Uri.TryCreate(baseUrl, UriKind.Absolute, out _) ? baseUrl : throw new InvalidOperationException("Jira configuration is not configured.");
    }

    private async Task<string> GetRequiredCredentialAsync(string key, string category, CancellationToken cancellationToken)
    {
        var credential = await credentials.GetAsync(key, cancellationToken);
        return !string.IsNullOrWhiteSpace(credential) ? credential : throw new InvalidOperationException($"{category} configuration is not configured.");
    }
}
