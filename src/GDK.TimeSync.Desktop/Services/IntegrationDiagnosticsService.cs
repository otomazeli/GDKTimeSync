using GDK.TimeSync.Jira;
using GDK.TimeSync.Tempo;

namespace GDK.TimeSync.Desktop.Services;

public sealed class IntegrationDiagnosticsService(IIntegrationClientFactory clients) : IIntegrationDiagnosticsService
{
    public async Task<IReadOnlyList<IntegrationDiagnosticResult>> RunAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return
        [
            await CheckAsync(IntegrationDiagnosticTarget.Toggl, clients.CreateTogglAsync,
                (client, token) => client.GetTimeEntriesAsync(today, today, token), cancellationToken),
            await CheckAsync(IntegrationDiagnosticTarget.Jira, clients.CreateJiraAsync,
                (client, token) => client.GetMyselfAsync(token), cancellationToken, DescribeCurrentUser),
            await CheckAsync(IntegrationDiagnosticTarget.Tempo, clients.CreateTempoAsync,
                (client, token) => client.GetWorkAttributesAsync(token), cancellationToken, DescribeWorkAttributes)
        ];
    }

    // The identity Tempo is sent as a worklog `worker`. Shown so a wrong one is visible here rather
    // than as a 400 at delivery time. Key and name only -- the email address is not needed to
    // diagnose this and this message gets copied into tickets.
    private static string DescribeCurrentUser(JiraCurrentUser user)
    {
        var parts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(user.Key)) parts.Add($"key={user.Key}");
        if (!string.IsNullOrWhiteSpace(user.Name)) parts.Add($"name={user.Name}");
        return parts.Count == 0 ? "Available" : $"Available: {string.Join(", ", parts)}";
    }

    // Tempo's work attributes are configuration, not data: naming them turns a bare "Available" into
    // the answer to "which id is the work-category attribute on this instance", which TempoClient
    // currently hardcodes. Names only -- nothing here is a credential, and nothing else is read.
    private static string DescribeWorkAttributes(IReadOnlyList<TempoAttribute> attributes) =>
        attributes.Count == 0
            ? "Available"
            : $"Available: {string.Join(", ", attributes.Take(MaxDescribedAttributes).Select(attribute => $"{attribute.Name} (id {attribute.Id})"))}";

    private const int MaxDescribedAttributes = 12;

    private static async Task<IntegrationDiagnosticResult> CheckAsync<TClient, TResult>(
        IntegrationDiagnosticTarget target,
        Func<CancellationToken, Task<TClient>> create,
        Func<TClient, CancellationToken, Task<TResult>> check,
        CancellationToken cancellationToken,
        Func<TResult, string>? describe = null)
        where TClient : IDisposable
    {
        TClient? client = default;
        IntegrationDiagnosticResult result;
        try
        {
            client = await create(cancellationToken);
            var checkResult = await check(client, cancellationToken);
            result = new IntegrationDiagnosticResult(target, true, describe?.Invoke(checkResult) ?? "Available");
        }
        catch (OperationCanceledException)
        {
            result = new IntegrationDiagnosticResult(target, false, "Cancelled");
        }
        catch
        {
            result = new IntegrationDiagnosticResult(target, false, "Unavailable");
        }
        finally
        {
            try
            {
                client?.Dispose();
            }
            catch
            {
                result = new IntegrationDiagnosticResult(target, false, "Unavailable");
            }
        }

        return result;
    }
}
