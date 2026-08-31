using System.Net.Http.Json;
using System.Text.Json;
namespace GDK.TimeSync.Slack;

public sealed class SlackClient : ISlackClient
{
    // PostAsJsonAsync's default naming policy is camelCase, but Slack Workflow Builder Data
    // Variable names are matched against the JSON keys case-sensitively as the user typed them
    // (e.g. "SlackTitle"), so the wire format must keep the exact PascalCase property names below.
    private static readonly JsonSerializerOptions PayloadOptions = new() { PropertyNamingPolicy = null };

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
            // Data Variables of a Workflow Builder "Webhook" trigger. TogglProject/JiraIssueKey/
            // Description/Status stay blank here: this app sends one combined daily digest (its
            // per-task lines already folded into SlackExtraLines by the composer), not one call
            // per task, but the fields are still sent so a workflow referencing them never sees
            // a missing key.
            using var response = await httpClient.PostAsJsonAsync("", new
            {
                update.SlackTitle,
                update.SlackTaskHeading,
                update.SlackExtraLines,
                TogglProject = "",
                JiraIssueKey = "",
                Description = "",
                Status = "",
                update.SlackUser
            }, PayloadOptions, cancellationToken);

            // A classic Incoming Webhook replies with the literal body "ok"; a Workflow Builder
            // webhook trigger does not use that format, so success is judged by status alone.
            if (!response.IsSuccessStatusCode)
            {
                failureCode = SlackFailureCode.UnsuccessfulResponse;
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
