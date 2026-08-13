using System.Net.Http.Json;
namespace GDK.TimeSync.Slack;

public sealed class SlackClient : ISlackClient
{
    private readonly HttpClient httpClient;

    public SlackClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (httpClient.BaseAddress is not { IsAbsoluteUri: true })
            throw new ArgumentException("Slack HTTP client must have an absolute base address.", nameof(httpClient));

        this.httpClient = httpClient;
    }

    public async Task PostAsync(SlackDailyUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        SlackFailureCode? failureCode = null;
        System.Net.HttpStatusCode? statusCode = null;
        try
        {
            using var response = await httpClient.PostAsJsonAsync("", new { text = update.Text }, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                failureCode = SlackFailureCode.UnsuccessfulResponse;
                statusCode = response.StatusCode;
            }

            else if (!string.Equals((await response.Content.ReadAsStringAsync(cancellationToken)).Trim(), "ok", StringComparison.Ordinal))
            {
                failureCode = SlackFailureCode.InvalidResponse;
                statusCode = response.StatusCode;
            }
        }
        catch (HttpRequestException)
        {
            throw new SlackApiException("Unable to reach Slack.", SlackFailureCode.Transport);
        }
        catch (OperationCanceledException)
        {
            throw new SlackApiException("Slack delivery was cancelled.", SlackFailureCode.Cancelled);
        }
        catch (Exception)
        {
            throw new SlackApiException("Unable to reach Slack.", SlackFailureCode.Transport);
        }

        if (failureCode is { } localFailureCode)
            throw new SlackApiException(
                localFailureCode == SlackFailureCode.UnsuccessfulResponse ? "Slack returned an unsuccessful response." : "Slack returned an invalid response.",
                localFailureCode,
                statusCode);
    }

    public void Dispose() => httpClient.Dispose();
}
