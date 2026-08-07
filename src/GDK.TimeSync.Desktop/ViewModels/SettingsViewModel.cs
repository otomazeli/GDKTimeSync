using System.ComponentModel;
using System.Runtime.CompilerServices;
using GDK.TimeSync.Desktop.Services;

namespace GDK.TimeSync.Desktop.ViewModels;

public sealed class SettingsViewModel(ICredentialStore credentials, IUserSettingsStore settings, IConfigurationStateService configurationState) : INotifyPropertyChanged
{
    private bool isTogglTokenConfigured;
    private bool isJiraPatConfigured;
    private bool isSaving;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsTogglTokenConfigured
    {
        get => isTogglTokenConfigured;
        private set => SetField(ref isTogglTokenConfigured, value);
    }

    public bool IsJiraPatConfigured
    {
        get => isJiraPatConfigured;
        private set => SetField(ref isJiraPatConfigured, value);
    }

    public bool IsSaving
    {
        get => isSaving;
        private set => SetField(ref isSaving, value);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await configurationState.RefreshAsync(cancellationToken);
        UpdateCredentialStatus();
    }

    public async Task SaveAsync(string jiraBaseUrl, string? newTogglToken, string? newJiraPat, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(jiraBaseUrl, UriKind.Absolute, out _))
            throw new ArgumentException("Enter an absolute Jira base URL.", nameof(jiraBaseUrl));

        IsSaving = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(newTogglToken))
            {
                try { await credentials.SaveAsync(CredentialKeys.TogglApiToken, newTogglToken, cancellationToken); }
                catch (Exception exception) { throw new SettingsSaveException("Unable to save Toggl credential.", exception); }
            }

            if (!string.IsNullOrWhiteSpace(newJiraPat))
            {
                try { await credentials.SaveAsync(CredentialKeys.JiraPat, newJiraPat, cancellationToken); }
                catch (Exception exception) { throw new SettingsSaveException("Unable to save CGM Jira credential.", exception); }
            }

            try { settings.Save(settings.Load() with { JiraBaseUrl = jiraBaseUrl }); }
            catch (Exception exception) { throw new SettingsSaveException("Credentials may have been saved, but non-secret settings could not be saved.", exception); }
            await configurationState.RefreshAsync(cancellationToken);
            UpdateCredentialStatus();
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void UpdateCredentialStatus()
    {
        IsTogglTokenConfigured = configurationState.HasTogglCredential;
        IsJiraPatConfigured = configurationState.HasJiraCredential;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
