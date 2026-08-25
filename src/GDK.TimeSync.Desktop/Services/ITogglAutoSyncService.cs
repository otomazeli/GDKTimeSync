namespace GDK.TimeSync.Desktop.Services;

public interface ITogglAutoSyncService
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
