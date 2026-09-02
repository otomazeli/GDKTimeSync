using System.Net;
using GDK.TimeSync.Jira;

namespace GDK.TimeSync.Desktop.Services;

// A read-only "what is this issue called?" lookup, kept separate from IIntegrationClientFactory so
// Today can be tested without standing up a JiraClient (which is sealed and needs a live handler).
public interface IJiraIssueLookup
{
    // Returns the issue summary, or null when Jira says the key does not exist. Any other failure --
    // unreachable, unauthorized, malformed key -- throws, and the caller decides how loud to be.
    Task<string?> GetSummaryAsync(string issueKey, CancellationToken cancellationToken = default);
}

public sealed class JiraIssueLookup(IIntegrationClientFactory clients) : IJiraIssueLookup
{
    public async Task<string?> GetSummaryAsync(string issueKey, CancellationToken cancellationToken = default)
    {
        using var jira = await clients.CreateJiraAsync(cancellationToken);
        try
        {
            return (await jira.GetIssueAsync(issueKey, cancellationToken)).Summary;
        }
        catch (JiraApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
