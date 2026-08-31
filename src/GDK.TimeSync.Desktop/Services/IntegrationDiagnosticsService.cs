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
                (client, token) => client.GetMyselfAsync(token), cancellationToken),
            await CheckAsync(IntegrationDiagnosticTarget.Tempo, clients.CreateTempoAsync,
                (client, token) => client.GetWorkAttributesAsync(token), cancellationToken)
        ];
    }

    private static async Task<IntegrationDiagnosticResult> CheckAsync<TClient, TResult>(
        IntegrationDiagnosticTarget target,
        Func<CancellationToken, Task<TClient>> create,
        Func<TClient, CancellationToken, Task<TResult>> check,
        CancellationToken cancellationToken)
        where TClient : IDisposable
    {
        TClient? client = default;
        IntegrationDiagnosticResult result;
        try
        {
            client = await create(cancellationToken);
            await check(client, cancellationToken);
            result = new IntegrationDiagnosticResult(target, true, "Available");
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
