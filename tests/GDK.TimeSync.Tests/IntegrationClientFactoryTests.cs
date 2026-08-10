using System.Net;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Jira;
using GDK.TimeSync.Tempo;
using GDK.TimeSync.Toggl;

namespace GDK.TimeSync.Tests;

public sealed class IntegrationClientFactoryTests
{
    [Fact]
    public async Task Create_clients_reads_credentials_only_when_invoked_and_sends_no_requests()
    {
        var credentials = new FakeCredentialStore
        {
            [CredentialKeys.TogglApiToken] = "toggl-test-token",
            [CredentialKeys.JiraPat] = "jira-test-pat"
        };
        var handler = new ThrowingHttpMessageHandler();
        var httpClients = new FakeHttpClientFactory(handler);
        var factory = new IntegrationClientFactory(
            credentials,
            new FakeSettingsStore(new UserSettings { JiraBaseUrl = "https://jira.example.test", TogglWorkspaceId = 42 }),
            httpClients,
            new GDK.TimeSync.Core.IssueKeyValidator(new GDK.TimeSync.Core.IssueKeyValidationOptions()));

        Assert.Empty(credentials.ReadKeys);

        var toggl = await factory.CreateTogglAsync();
        var jira = await factory.CreateJiraAsync();
        var tempo = await factory.CreateTempoAsync();

        Assert.IsType<TogglClient>(toggl);
        Assert.IsType<JiraClient>(jira);
        Assert.IsType<TempoClient>(tempo);
        Assert.Equal(
            [CredentialKeys.TogglApiToken, CredentialKeys.JiraPat, CredentialKeys.JiraPat],
            credentials.ReadKeys);
        Assert.Equal(
            [
                IntegrationClientFactory.TogglHttpClientName,
                IntegrationClientFactory.JiraHttpClientName,
                IntegrationClientFactory.TempoHttpClientName
            ],
            httpClients.RequestedNames);
        Assert.Equal(0, handler.RequestCount);
        AssertNoReadableSecretProperties(typeof(IntegrationClientFactory), typeof(TogglClient), typeof(JiraClient), typeof(TempoClient));
    }

    [Fact]
    public async Task Create_jira_rejects_invalid_configuration_without_disclosing_values()
    {
        const string secret = "do-not-disclose";
        const string invalidUrl = "not-a-url";
        var factory = new IntegrationClientFactory(
            new FakeCredentialStore { [CredentialKeys.JiraPat] = secret },
            new FakeSettingsStore(new UserSettings { JiraBaseUrl = invalidUrl }),
            new FakeHttpClientFactory(new ThrowingHttpMessageHandler()),
            new GDK.TimeSync.Core.IssueKeyValidator(new GDK.TimeSync.Core.IssueKeyValidationOptions()));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateJiraAsync());

        Assert.Equal("Jira configuration is not configured.", exception.Message);
        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(invalidUrl, exception.ToString(), StringComparison.Ordinal);
    }

    private static void AssertNoReadableSecretProperties(params Type[] types) =>
        Assert.All(types, type => Assert.DoesNotContain(type.GetProperties(), property =>
            property.PropertyType == typeof(string) &&
            (property.Name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
             property.Name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
             property.Name.Contains("pat", StringComparison.OrdinalIgnoreCase))));

    private sealed class FakeCredentialStore : ICredentialStore
    {
        private readonly Dictionary<string, string> values = [];

        public List<string> ReadKeys { get; } = [];

        public string this[string key]
        {
            set => values[key] = value;
        }

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            ReadKeys.Add(key);
            return Task.FromResult(values.GetValueOrDefault(key));
        }

        public Task SaveAsync(string key, string secret, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(values.ContainsKey(key));

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSettingsStore(UserSettings settings) : IUserSettingsStore
    {
        public UserSettings Load() => settings;

        public void Save(UserSettings settings) { }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public List<string> RequestedNames { get; } = [];

        public HttpClient CreateClient(string name)
        {
            RequestedNames.Add(name);
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}
