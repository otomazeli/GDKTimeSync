using System.Net;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Jira;
using GDK.TimeSync.Tempo;
using GDK.TimeSync.Toggl;

namespace GDK.TimeSync.Tests;

public sealed class IntegrationClientFactoryTests
{
    [Fact]
    public void Construction_does_not_resolve_http_or_issue_key_services()
    {
        var factory = new IntegrationClientFactory(new FakeCredentialStore(), new FakeSettingsStore(new UserSettings()), null, null);

        Assert.NotNull(factory);
    }

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

        using var toggl = await factory.CreateTogglAsync();
        using var jira = await factory.CreateJiraAsync();
        using var tempo = await factory.CreateTempoAsync();

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

    [Theory]
    [InlineData("Toggl")]
    [InlineData("Jira")]
    [InlineData("Tempo")]
    public async Task Create_with_a_missing_credential_does_not_allocate_an_http_client(string integration)
    {
        var httpClients = new FakeHttpClientFactory(new ThrowingHttpMessageHandler());
        var factory = CreateFactory(new FakeCredentialStore(), httpClients);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateAsync(factory, integration));

        Assert.Equal($"{integration} configuration is not configured.", exception.Message);
        Assert.Empty(httpClients.RequestedNames);
    }

    [Fact]
    public async Task Create_with_a_cancelled_token_forwards_cancellation_without_allocating_an_http_client()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var credentials = new FakeCredentialStore { [CredentialKeys.TogglApiToken] = "toggl-test-token" };
        var httpClients = new FakeHttpClientFactory(new ThrowingHttpMessageHandler());
        var factory = CreateFactory(credentials, httpClients);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => factory.CreateTogglAsync(cancellation.Token));

        Assert.Equal([cancellation.Token], credentials.ReadCancellationTokens);
        Assert.Empty(httpClients.RequestedNames);
    }

    [Fact]
    public async Task Factory_created_clients_dispose_their_httpclient_without_disposing_the_handler_pool()
    {
        var handler = new ThrowingHttpMessageHandler();
        var httpClients = new FakeHttpClientFactory(handler);
        var factory = CreateFactory(
            new FakeCredentialStore
            {
                [CredentialKeys.TogglApiToken] = "toggl-test-token",
                [CredentialKeys.JiraPat] = "jira-test-pat"
            },
            httpClients);

        var toggl = await factory.CreateTogglAsync();
        var jira = await factory.CreateJiraAsync();
        var tempo = await factory.CreateTempoAsync();

        toggl.Dispose();
        jira.Dispose();
        tempo.Dispose();

        Assert.All(httpClients.CreatedClients, client => Assert.True(client.WasDisposed));
        Assert.False(handler.WasDisposed);
        Assert.Equal(0, handler.RequestCount);
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

    private static IntegrationClientFactory CreateFactory(FakeCredentialStore credentials, FakeHttpClientFactory httpClients) =>
        new(
            credentials,
            new FakeSettingsStore(new UserSettings { JiraBaseUrl = "https://jira.example.test" }),
            httpClients,
            new GDK.TimeSync.Core.IssueKeyValidator(new GDK.TimeSync.Core.IssueKeyValidationOptions()));

    private static Task CreateAsync(IIntegrationClientFactory factory, string integration) => integration switch
    {
        "Toggl" => factory.CreateTogglAsync(),
        "Jira" => factory.CreateJiraAsync(),
        "Tempo" => factory.CreateTempoAsync(),
        _ => throw new ArgumentOutOfRangeException(nameof(integration))
    };

    private sealed class FakeCredentialStore : ICredentialStore
    {
        private readonly Dictionary<string, string> values = [];

        public List<string> ReadKeys { get; } = [];
        public List<CancellationToken> ReadCancellationTokens { get; } = [];

        public string this[string key]
        {
            set => values[key] = value;
        }

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            ReadKeys.Add(key);
            ReadCancellationTokens.Add(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
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
        public List<TrackingHttpClient> CreatedClients { get; } = [];

        public HttpClient CreateClient(string name)
        {
            RequestedNames.Add(name);
            var client = new TrackingHttpClient(handler);
            CreatedClients.Add(client);
            return client;
        }
    }

    private sealed class TrackingHttpClient(HttpMessageHandler handler) : HttpClient(handler, disposeHandler: false)
    {
        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public bool WasDisposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}
