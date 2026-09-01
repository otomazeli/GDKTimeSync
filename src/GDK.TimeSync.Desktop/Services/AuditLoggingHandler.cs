using System.Diagnostics;
using System.Net.Http;
using GDK.TimeSync.Core;

namespace GDK.TimeSync.Desktop.Services;

// Registered on the named HttpClient registrations in App.ConfigureServices, so every Toggl, Jira,
// Tempo, and Slack call is captured without touching a single API client.
public sealed class AuditLoggingHandler(IAuditLog auditLog, string clientName, bool redactUri = false) : DelegatingHandler
{
    private const int MaxBodyCharacters = 4000;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        // Only the method and path are read. Headers are never enumerated, so the Authorization
        // header carrying the Toggl token / Jira PAT cannot reach the log by any path.
        var target = $"{request.Method.Method} {DescribeUri(request.RequestUri)}";
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            stopwatch.Stop();
            var line = $"{target} -> {(int)response.StatusCode} {response.StatusCode} ({stopwatch.ElapsedMilliseconds} ms)";
            if (response.IsSuccessStatusCode)
                auditLog.Write(AuditLevel.Info, clientName, line);
            else
                auditLog.Write(AuditLevel.Error, clientName, $"{line}{Environment.NewLine}response: {await ReadAndReplaceBodyAsync(response, cancellationToken)}");
            return response;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            auditLog.Write(AuditLevel.Error, clientName, $"{target} -> transport failure after {stopwatch.ElapsedMilliseconds} ms: {exception.GetType().Name}");
            throw;
        }
    }

    // The Slack webhook URL is itself the credential -- SlackClient posts to "" against a
    // BaseAddress that is the secret trigger URL -- so for Slack no part of the URI is written.
    private string DescribeUri(Uri? uri) =>
        redactUri ? "<slack webhook>" : uri?.AbsolutePath ?? "(no uri)";

    // Reads the body for the log line, then swaps in a fresh, replayable copy of the content.
    // HttpClient itself re-buffers the response content after this handler returns (the default
    // ResponseContentRead completion option), and the real API client still needs to read the
    // body too -- so the original (possibly single-read, possibly faulted) content must not be
    // left behind exhausted.
    private static async Task<string> ReadAndReplaceBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var original = response.Content;
        string body;
        try
        {
            body = await original.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            response.Content = new StringContent(string.Empty);
            original.Dispose();
            return "(response body could not be read)";
        }

        var replacement = new StringContent(body);
        if (original.Headers.ContentType is { } contentType)
            replacement.Headers.ContentType = contentType;
        response.Content = replacement;
        original.Dispose();

        return body.Length > MaxBodyCharacters ? body[..MaxBodyCharacters] + " …(truncated)" : body;
    }
}
