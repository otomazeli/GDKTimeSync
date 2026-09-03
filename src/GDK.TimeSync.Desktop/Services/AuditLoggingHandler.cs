using System.Diagnostics;
using System.Text.Json;
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
                auditLog.Write(AuditLevel.Error, clientName, $"{line}{Environment.NewLine}response: {await DescribeFailureAsync(response, cancellationToken)}");
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
    // The failure body is suppressed wholesale for the same client: a corporate proxy block page
    // echoes the requested URL back inside the body, which would put the credential in the log.
    private string DescribeUri(Uri? uri) =>
        redactUri ? "<slack webhook>" : uri?.AbsolutePath ?? "(no uri)";

    // For the redacted client the whole body stays suppressed, with one exception: Slack's own error
    // JSON. `{"ok":false,"error":"webhook_not_found"}` names the fault and structurally cannot carry
    // the webhook URL, because only that one known field is read -- a proxy block page echoing the
    // URL is not JSON with an `error` string, so it never survives this filter. Getting
    // "webhook_not_found" instead of "(redacted)" is the difference between knowing the trigger is
    // gone and having to go and find out.
    private async Task<string> DescribeFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(response, cancellationToken);
        if (!redactUri) return body;
        return ExtractErrorCode(body) is { } code ? $"error: {code}" : "(redacted)";
    }

    private static string? ExtractErrorCode(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!document.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.String) return null;

            var code = error.GetString();
            // An error *code* is a short token. Anything longer is prose that could quote the request.
            return code is { Length: > 0 and <= MaxErrorCodeCharacters } ? code : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private const int MaxErrorCodeCharacters = 64;

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            // ReadAsStringAsync buffers the content internally, so the caller's own later reads --
            // and HttpClient's post-handler re-buffer -- replay from that buffer. Reading here does
            // not starve any downstream consumer, and the response is returned untouched.
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return body.Length > MaxBodyCharacters ? body[..MaxBodyCharacters] + " …(truncated)" : body;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Content that cannot be buffered at all would throw again in HttpClient's own re-buffer,
            // *after* this handler returns and outside any catch of ours. Swapping in empty content
            // keeps that failure from surfacing to the caller as a torn response.
            var unreadable = response.Content;
            response.Content = new StringContent("");
            unreadable.Dispose();
            return "(response body could not be read)";
        }
    }
}
