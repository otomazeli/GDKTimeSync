namespace GDK.TimeSync.Desktop.Services;

public interface IIntegrationDiagnosticsService
{
    Task<IReadOnlyList<IntegrationDiagnosticResult>> RunAsync(CancellationToken cancellationToken = default);
}
