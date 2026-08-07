namespace GDK.TimeSync.Desktop.Services;

public interface IConfigurationStateService
{
    bool IsConfigured { get; }
    bool HasTogglCredential { get; }
    bool HasJiraCredential { get; }
    event EventHandler? ConfigurationChanged;
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
