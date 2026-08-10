using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using GDK.TimeSync.Desktop.Services;

namespace GDK.TimeSync.Desktop.ViewModels;

public sealed class SettingsViewModel(ICredentialStore credentials, IUserSettingsStore settings, IConfigurationStateService configurationState) : INotifyPropertyChanged
{
    private bool isTogglTokenConfigured;
    private bool isJiraPatConfigured;
    private bool isSlackWebhookConfigured;
    private bool isSaving;
    private string jiraBaseUrl = string.Empty;
    private long? togglWorkspaceId;
    private string reviewReminderTime = "16:00";
    private string defaultTempoWorkCategory = "DEVELOPMENT";
    private bool aiEnabled;

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

    public bool IsSlackWebhookConfigured
    {
        get => isSlackWebhookConfigured;
        private set => SetField(ref isSlackWebhookConfigured, value);
    }

    public string JiraBaseUrl
    {
        get => jiraBaseUrl;
        set => SetField(ref jiraBaseUrl, value);
    }

    public long? TogglWorkspaceId
    {
        get => togglWorkspaceId;
        set => SetField(ref togglWorkspaceId, value);
    }

    public string ReviewReminderTime
    {
        get => reviewReminderTime;
        set => SetField(ref reviewReminderTime, value);
    }

    public string DefaultTempoWorkCategory
    {
        get => defaultTempoWorkCategory;
        set => SetField(ref defaultTempoWorkCategory, value);
    }

    public bool AiEnabled
    {
        get => aiEnabled;
        set => SetField(ref aiEnabled, value);
    }

    public bool IsSaving
    {
        get => isSaving;
        private set => SetField(ref isSaving, value);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        LoadNonSecretSettings(settings.Load());
        await configurationState.RefreshAsync(cancellationToken);
        await UpdateCredentialStatusAsync(cancellationToken);
    }

    public async Task SaveAsync(string jiraBaseUrl, string? newTogglToken, string? newJiraPat, CancellationToken cancellationToken = default)
        => await SaveAsync(jiraBaseUrl, newTogglToken, newJiraPat, null, cancellationToken);

    public async Task SaveAsync(string jiraBaseUrl, string? newTogglToken, string? newJiraPat, string? newSlackWebhook, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(jiraBaseUrl, UriKind.Absolute, out _))
            throw new ArgumentException("Enter an absolute Jira base URL.", nameof(jiraBaseUrl));
        if (!TimeOnly.TryParseExact(ReviewReminderTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var reviewTime))
            throw new ArgumentException("Enter review reminder time as HH:mm.", nameof(ReviewReminderTime));

        var normalizedJiraBaseUrl = jiraBaseUrl.Trim();
        var normalizedReviewReminderTime = reviewTime.ToString("HH:mm", CultureInfo.InvariantCulture);

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

            if (!string.IsNullOrWhiteSpace(newSlackWebhook))
            {
                try { await credentials.SaveAsync(CredentialKeys.SlackWebhook, newSlackWebhook, cancellationToken); }
                catch (Exception exception) { throw new SettingsSaveException("Unable to save Slack credential.", exception); }
            }

            try
            {
                settings.Save(settings.Load() with
                {
                    JiraBaseUrl = normalizedJiraBaseUrl,
                    TogglWorkspaceId = TogglWorkspaceId,
                    ReviewReminderTime = normalizedReviewReminderTime,
                    DefaultTempoWorkCategory = DefaultTempoWorkCategory.Trim(),
                    AiEnabled = AiEnabled
                });
            }
            catch (Exception exception) { throw new SettingsSaveException("Credentials may have been saved, but non-secret settings could not be saved.", exception); }
            await configurationState.RefreshAsync(cancellationToken);
            await UpdateCredentialStatusAsync(cancellationToken);
            JiraBaseUrl = normalizedJiraBaseUrl;
            ReviewReminderTime = normalizedReviewReminderTime;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void LoadNonSecretSettings(UserSettings currentSettings)
    {
        JiraBaseUrl = currentSettings.JiraBaseUrl;
        TogglWorkspaceId = currentSettings.TogglWorkspaceId;
        ReviewReminderTime = currentSettings.ReviewReminderTime;
        DefaultTempoWorkCategory = currentSettings.DefaultTempoWorkCategory;
        AiEnabled = currentSettings.AiEnabled;
    }

    private async Task UpdateCredentialStatusAsync(CancellationToken cancellationToken)
    {
        IsTogglTokenConfigured = configurationState.HasTogglCredential;
        IsJiraPatConfigured = configurationState.HasJiraCredential;
        IsSlackWebhookConfigured = await credentials.ExistsAsync(CredentialKeys.SlackWebhook, cancellationToken);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
