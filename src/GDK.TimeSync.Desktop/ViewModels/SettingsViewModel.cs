using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using GDK.TimeSync.Desktop.Services;

namespace GDK.TimeSync.Desktop.ViewModels;

public sealed record ReminderModeOption(EndOfDayReminderMode Value, string Label);

public sealed class SettingsViewModel(ICredentialStore credentials, IUserSettingsStore settings, IConfigurationStateService configurationState) : INotifyPropertyChanged
{
    private bool isTogglTokenConfigured;
    private bool isJiraPatConfigured;
    private bool isSlackWebhookConfigured;
    private bool isSaving;
    private string jiraBaseUrl = string.Empty;
    private string jiraUser = string.Empty;
    private long? togglWorkspaceId;
    private string reviewReminderTime = "16:00";
    private EndOfDayReminderMode endOfDayReminderMode = EndOfDayReminderMode.Both;
    private string defaultTempoWorkCategory = "DEVELOPMENT";
    private string slackTitle = "Daily update";
    private string slackTaskHeading = "Completed tasks";
    private string slackExtraLines = string.Empty;
    private bool aiEnabled;
    private bool autoSyncEnabled = true;
    private int syncIntervalMinutes = 5;

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

    public string JiraUser
    {
        get => jiraUser;
        set => SetField(ref jiraUser, value);
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

    public IReadOnlyList<ReminderModeOption> ReminderModeOptions { get; } =
    [
        new(EndOfDayReminderMode.TrayNotificationOnly, "Tray notification only"),
        new(EndOfDayReminderMode.OpenReviewOnly, "Open Review window only"),
        new(EndOfDayReminderMode.Both, "Both")
    ];

    public EndOfDayReminderMode EndOfDayReminderMode
    {
        get => endOfDayReminderMode;
        set => SetField(ref endOfDayReminderMode, value);
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

    public bool AutoSyncEnabled
    {
        get => autoSyncEnabled;
        set => SetField(ref autoSyncEnabled, value);
    }

    public int SyncIntervalMinutes
    {
        get => syncIntervalMinutes;
        set => SetField(ref syncIntervalMinutes, value);
    }

    public string SlackTitle { get => slackTitle; set => SetField(ref slackTitle, value); }
    public string SlackTaskHeading { get => slackTaskHeading; set => SetField(ref slackTaskHeading, value); }
    public string SlackExtraLines { get => slackExtraLines; set => SetField(ref slackExtraLines, value); }

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

    public Task SaveAsync(string jiraBaseUrl, string? newTogglToken, string? newJiraPat, CancellationToken cancellationToken = default)
        => SaveAsync(settings.Load() with
        {
            JiraBaseUrl = jiraBaseUrl,
            JiraUser = JiraUser,
            TogglWorkspaceId = TogglWorkspaceId,
            ReviewReminderTime = ReviewReminderTime,
            EndOfDayReminderMode = EndOfDayReminderMode,
            DefaultTempoWorkCategory = DefaultTempoWorkCategory,
            AiEnabled = AiEnabled,
            AutoSyncEnabled = AutoSyncEnabled,
            SyncIntervalMinutes = SyncIntervalMinutes,
            SlackTitle = SlackTitle,
            SlackTaskHeading = SlackTaskHeading,
            SlackExtraLines = SplitSlackLines(SlackExtraLines)
        }, newTogglToken, newJiraPat, null, cancellationToken);

    public Task SaveAsync(string jiraBaseUrl, string? newTogglToken, string? newJiraPat, string? newSlackWebhook, CancellationToken cancellationToken = default)
        => SaveAsync(settings.Load() with
        {
            JiraBaseUrl = jiraBaseUrl,
            JiraUser = JiraUser,
            TogglWorkspaceId = TogglWorkspaceId,
            ReviewReminderTime = ReviewReminderTime,
            EndOfDayReminderMode = EndOfDayReminderMode,
            DefaultTempoWorkCategory = DefaultTempoWorkCategory,
            AiEnabled = AiEnabled,
            AutoSyncEnabled = AutoSyncEnabled,
            SyncIntervalMinutes = SyncIntervalMinutes,
            SlackTitle = SlackTitle,
            SlackTaskHeading = SlackTaskHeading,
            SlackExtraLines = SplitSlackLines(SlackExtraLines)
        }, newTogglToken, newJiraPat, newSlackWebhook, cancellationToken);

    public async Task SaveAsync(UserSettings proposedSettings, string? newTogglToken, string? newJiraPat, string? newSlackWebhook, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposedSettings);
        if (!Uri.TryCreate(proposedSettings.JiraBaseUrl, UriKind.Absolute, out _))
            throw new ArgumentException("Enter an absolute Jira base URL.", nameof(proposedSettings));
        var normalizedJiraUser = proposedSettings.JiraUser.Trim();
        if (!string.IsNullOrEmpty(normalizedJiraUser) && !IsEmailAddress(normalizedJiraUser))
            throw new ArgumentException("Enter a valid Jira user email address.", nameof(proposedSettings));
        if (!TimeOnly.TryParseExact(proposedSettings.ReviewReminderTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var reviewTime))
            throw new ArgumentException("Enter review reminder time as HH:mm.", nameof(proposedSettings));
        if (proposedSettings.SyncIntervalMinutes < 1)
            throw new ArgumentException("Enter a sync interval of at least 1 minute.", nameof(proposedSettings));

        var normalizedJiraBaseUrl = proposedSettings.JiraBaseUrl.Trim();
        var normalizedReviewReminderTime = reviewTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        var normalizedSettings = proposedSettings with
        {
            JiraBaseUrl = normalizedJiraBaseUrl,
            JiraUser = normalizedJiraUser,
            ReviewReminderTime = normalizedReviewReminderTime,
            EndOfDayReminderMode = EndOfDayReminderModes.Normalize(proposedSettings.EndOfDayReminderMode),
            DefaultTempoWorkCategory = proposedSettings.DefaultTempoWorkCategory.Trim(),
            SlackTitle = NormalizeSlackText(proposedSettings.SlackTitle),
            SlackTaskHeading = NormalizeSlackText(proposedSettings.SlackTaskHeading),
            SlackExtraLines = proposedSettings.SlackExtraLines.Select(NormalizeSlackText).Where(line => line.Length > 0).ToArray()
        };
        UserSettingsService.ValidateSlackPresentation(normalizedSettings);

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
                settings.Save(normalizedSettings);
            }
            catch (Exception exception) { throw new SettingsSaveException("Credentials may have been saved, but non-secret settings could not be saved.", exception); }
            await configurationState.RefreshAsync(cancellationToken);
            await UpdateCredentialStatusAsync(cancellationToken);
            LoadNonSecretSettings(normalizedSettings);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void LoadNonSecretSettings(UserSettings currentSettings)
    {
        JiraBaseUrl = currentSettings.JiraBaseUrl;
        JiraUser = currentSettings.JiraUser;
        TogglWorkspaceId = currentSettings.TogglWorkspaceId;
        ReviewReminderTime = currentSettings.ReviewReminderTime;
        EndOfDayReminderMode = EndOfDayReminderModes.Normalize(currentSettings.EndOfDayReminderMode);
        DefaultTempoWorkCategory = currentSettings.DefaultTempoWorkCategory;
        AiEnabled = currentSettings.AiEnabled;
        AutoSyncEnabled = currentSettings.AutoSyncEnabled;
        SyncIntervalMinutes = currentSettings.SyncIntervalMinutes;
        SlackTitle = currentSettings.SlackTitle;
        SlackTaskHeading = currentSettings.SlackTaskHeading;
        SlackExtraLines = string.Join(Environment.NewLine, currentSettings.SlackExtraLines);
    }

    private async Task UpdateCredentialStatusAsync(CancellationToken cancellationToken)
    {
        IsTogglTokenConfigured = configurationState.HasTogglCredential;
        IsJiraPatConfigured = configurationState.HasJiraCredential;
        IsSlackWebhookConfigured = await credentials.ExistsAsync(CredentialKeys.SlackWebhook, cancellationToken);
    }

    private static bool IsEmailAddress(string value)
    {
        try { return new System.Net.Mail.MailAddress(value).Address == value; }
        catch (FormatException) { return false; }
    }

    private static IReadOnlyList<string> SplitSlackLines(string value) => value.Split(["\r\n", "\n"], StringSplitOptions.None);

    private static string NormalizeSlackText(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
