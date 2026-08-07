using System.Diagnostics;

namespace GDK.TimeSync.Desktop.Services;

public sealed class ConfigurationStateService(ICredentialStore credentials, IUserSettingsStore settings) : IConfigurationStateService
{
    public bool IsConfigured { get; private set; }
    public bool HasTogglCredential { get; private set; }
    public bool HasJiraCredential { get; private set; }

    public event EventHandler? ConfigurationChanged;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        HasTogglCredential = await credentials.ExistsAsync(CredentialKeys.TogglApiToken, cancellationToken);
        HasJiraCredential = await credentials.ExistsAsync(CredentialKeys.JiraPat, cancellationToken);
        var currentSettings = settings.Load();
        IsConfigured = HasTogglCredential && HasJiraCredential && currentSettings.IsConfigured;
        Trace.WriteLine($"GDK TimeSync configuration refreshed. Toggl={HasTogglCredential}; Jira={HasJiraCredential}; Configured={IsConfigured}.");
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
    }
}
