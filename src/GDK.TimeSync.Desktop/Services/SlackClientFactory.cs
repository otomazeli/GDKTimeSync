using System.Net.Http;
using GDK.TimeSync.Slack;

namespace GDK.TimeSync.Desktop.Services;

public sealed class SlackClientFactory(ICredentialStore credentials, IHttpClientFactory httpClientFactory) : ISlackClientFactory
{
    public const string HttpClientName = "GDK.TimeSync.Slack";

    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await credentials.ExistsAsync(CredentialKeys.SlackWebhook, cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    public async Task<ISlackClient> CreateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var webhook = await credentials.GetAsync(CredentialKeys.SlackWebhook, cancellationToken);
            if (!Uri.TryCreate(webhook, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException();

            var client = httpClientFactory.CreateClient(HttpClientName);
            client.BaseAddress = uri;
            return new SlackClient(client);
        }
        catch
        {
            throw new InvalidOperationException("Slack configuration is not configured.");
        }
    }

}
