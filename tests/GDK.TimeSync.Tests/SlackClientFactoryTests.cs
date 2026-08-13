using System.Net;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Slack;

namespace GDK.TimeSync.Tests;

public sealed class SlackClientFactoryTests
{
    [Fact]
    public async Task CreateAsync_reads_the_webhook_only_when_final_send_creates_a_client()
    {
        var credentials = new RecordingCredentials(CredentialKeys.SlackWebhook, "https://hooks.slack.com/services/unit-test");
        var httpClients = new RecordingHttpClientFactory();
        var factory = new SlackClientFactory(credentials, httpClients);

        Assert.Empty(credentials.ReadKeys);
        using var client = await factory.CreateAsync();

        Assert.Equal([CredentialKeys.SlackWebhook], credentials.ReadKeys);
        Assert.Equal([SlackClientFactory.HttpClientName], httpClients.Names);
        Assert.IsAssignableFrom<ISlackClient>(client);
    }

    [Theory]
    [InlineData("http://hooks.slack.com/services/unit-test")]
    [InlineData("not-a-url")]
    public async Task CreateAsync_rejects_invalid_webhook_without_exposing_its_value(string webhook)
    {
        var factory = new SlackClientFactory(new RecordingCredentials(CredentialKeys.SlackWebhook, webhook), new RecordingHttpClientFactory());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateAsync());

        Assert.Equal("Slack configuration is not configured.", exception.Message);
        Assert.DoesNotContain(webhook, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Configuration_preflight_uses_credential_presence_without_reading_or_creating_a_client()
    {
        var httpClients = new RecordingHttpClientFactory();
        var missingCredentials = new RecordingCredentials(CredentialKeys.SlackWebhook, null);
        var invalidCredentials = new RecordingCredentials(CredentialKeys.SlackWebhook, "not-a-url");
        var missing = new SlackClientFactory(missingCredentials, httpClients);
        var invalid = new SlackClientFactory(invalidCredentials, httpClients);

        Assert.False(await missing.IsConfiguredAsync());
        Assert.True(await invalid.IsConfiguredAsync());
        Assert.Empty(missingCredentials.ReadKeys);
        Assert.Empty(invalidCredentials.ReadKeys);
        Assert.Equal(1, missingCredentials.ExistsCalls);
        Assert.Equal(1, invalidCredentials.ExistsCalls);
        Assert.Empty(httpClients.Names);
    }

    [Fact]
    public async Task CreateAsync_sanitizes_credential_and_http_factory_exceptions()
    {
        const string sentinel = "https://hooks.slack.com/services/sentinel-secret";
        var factory = new SlackClientFactory(new ThrowingCredentials(sentinel), new RecordingHttpClientFactory());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateAsync());

        Assert.Equal("Slack configuration is not configured.", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(sentinel, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_sanitizes_http_client_factory_exceptions()
    {
        const string sentinel = "https://hooks.slack.com/services/sentinel-secret";
        var factory = new SlackClientFactory(new RecordingCredentials(CredentialKeys.SlackWebhook, "https://hooks.slack.com/services/valid"), new ThrowingHttpClientFactory(sentinel));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateAsync());

        Assert.Equal("Slack configuration is not configured.", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(sentinel, exception.ToString(), StringComparison.Ordinal);
    }

    private sealed class RecordingCredentials(string key, string? value) : ICredentialStore
    {
        public List<string> ReadKeys { get; } = [];
        public int ExistsCalls { get; private set; }
        public Task<string?> GetAsync(string requestedKey, CancellationToken cancellationToken = default) { ReadKeys.Add(requestedKey); return Task.FromResult<string?>(requestedKey == key ? value : null); }
        public Task SaveAsync(string key, string secret, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string requestedKey, CancellationToken cancellationToken = default) { ExistsCalls++; return Task.FromResult(requestedKey == key && value is not null); }
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ThrowingCredentials(string sentinel) : ICredentialStore
    {
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => throw new InvalidOperationException(sentinel);
        public Task SaveAsync(string key, string secret, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingHttpClientFactory : IHttpClientFactory
    {
        public List<string> Names { get; } = [];
        public HttpClient CreateClient(string name) { Names.Add(name); return new HttpClient(new HttpClientHandler(), false); }
    }

    private sealed class ThrowingHttpClientFactory(string sentinel) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException(sentinel);
    }
}
