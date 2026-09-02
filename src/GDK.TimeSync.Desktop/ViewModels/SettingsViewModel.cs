using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Toggl;

namespace GDK.TimeSync.Desktop.ViewModels;

public sealed record ReminderModeOption(EndOfDayReminderMode Value, string Label);

public sealed class SettingsViewModel(ICredentialStore credentials, IUserSettingsStore settings, IConfigurationStateService configurationState, IAuditLog? auditLog = null, IIntegrationClientFactory? integrationClients = null) : INotifyPropertyChanged
{
    private bool isTogglTokenConfigured;
    private bool isJiraPatConfigured;
    private bool isSlackWebhookConfigured;
    private bool isSaving;
    private string jiraBaseUrl = string.Empty;
    private string jiraUser = string.Empty;
    private long? togglWorkspaceId;
    private long? defaultTogglProjectId;
    private string reviewReminderTime = "16:00";
    private EndOfDayReminderMode endOfDayReminderMode = EndOfDayReminderMode.Both;
    private string defaultTempoWorkCategory = "DEVELOPMENT";
    private string defaultTogglProject = "";
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

    // Populated from Toggl so the default cannot be a name that matches nothing. Empty when the
    // workspace or token is not configured yet -- which is a normal state for this window, since it
    // is where those get entered.
    public ObservableCollection<TogglProject> AvailableProjects { get; } = [];
    public string? ProjectLoadError { get; private set; }

    public long? DefaultTogglProjectId
    {
        get => defaultTogglProjectId;
        set
        {
            if (defaultTogglProjectId == value) return;
            SetField(ref defaultTogglProjectId, value);
            // Keep the readable half in step with the chosen id.
            var chosen = AvailableProjects.FirstOrDefault(project => project.Id == value);
            if (chosen is not null) DefaultTogglProject = chosen.Name;
        }
    }

    public async Task LoadProjectsAsync(CancellationToken cancellationToken = default)
    {
        ProjectLoadError = null;
        if (integrationClients is null) return;

        try
        {
            var workspaceId = settings.Load().TogglWorkspaceId;
            if (workspaceId is not > 0)
            {
                ProjectLoadError = "Enter a Toggl workspace ID and save, then reopen to pick a project.";
                return;
            }

            using var toggl = await integrationClients.CreateTogglAsync(cancellationToken);
            AvailableProjects.Clear();
            foreach (var project in await toggl.GetProjectsAsync(workspaceId.Value, cancellationToken))
                AvailableProjects.Add(project);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Unreachable Toggl must not stop the rest of settings being edited or saved.
            ProjectLoadError = "Could not load Toggl projects. The stored default is kept.";
        }
    }

    public string DefaultTogglProject
    {
        get => defaultTogglProject;
        set => SetField(ref defaultTogglProject, value);
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
            DefaultTogglProject = DefaultTogglProject,
            DefaultTogglProjectId = DefaultTogglProjectId,
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
            DefaultTogglProject = DefaultTogglProject,
            DefaultTogglProjectId = DefaultTogglProjectId,
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
            DefaultTogglProject = proposedSettings.DefaultTogglProject.Trim(),
            DefaultTogglProjectId = proposedSettings.DefaultTogglProjectId,
            SlackTitle = NormalizeSlackText(proposedSettings.SlackTitle),
            SlackTaskHeading = NormalizeSlackText(proposedSettings.SlackTaskHeading),
            SlackExtraLines = proposedSettings.SlackExtraLines.Select(NormalizeSlackText).Where(line => line.Length > 0).ToArray()
        };
        UserSettingsService.ValidateSlackPresentation(normalizedSettings);

        // Read purely to name the changed fields in the audit entry. A diagnostic must never be
        // able to abort the save -- Load() only catches JsonException, so an IO or access failure
        // reading settings.json would otherwise take the credential writes down with it.
        UserSettings previousSettings;
        try { previousSettings = settings.Load(); }
        catch { previousSettings = new UserSettings(); }

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
            auditLog?.Write(AuditLevel.Info, "Settings", $"Saved: {DescribeChangedFields(previousSettings, normalizedSettings)}");
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
        DefaultTogglProject = currentSettings.DefaultTogglProject;
        DefaultTogglProjectId = currentSettings.DefaultTogglProjectId;
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

    // Field names only, never values -- the point is an audit trail of what changed, not a record of
    // Jira URLs, workspace IDs, or Slack presentation text.
    private static string DescribeChangedFields(UserSettings previous, UserSettings updated)
    {
        var changed = new List<string>();
        if (previous.JiraBaseUrl != updated.JiraBaseUrl) changed.Add(nameof(UserSettings.JiraBaseUrl));
        if (previous.JiraUser != updated.JiraUser) changed.Add(nameof(UserSettings.JiraUser));
        if (previous.TogglWorkspaceId != updated.TogglWorkspaceId) changed.Add(nameof(UserSettings.TogglWorkspaceId));
        if (previous.ReviewReminderTime != updated.ReviewReminderTime) changed.Add(nameof(UserSettings.ReviewReminderTime));
        if (previous.EndOfDayReminderMode != updated.EndOfDayReminderMode) changed.Add(nameof(UserSettings.EndOfDayReminderMode));
        if (previous.DefaultTempoWorkCategory != updated.DefaultTempoWorkCategory) changed.Add(nameof(UserSettings.DefaultTempoWorkCategory));
        if (previous.DefaultTogglProject != updated.DefaultTogglProject) changed.Add(nameof(UserSettings.DefaultTogglProject));
        if (previous.DefaultTogglProjectId != updated.DefaultTogglProjectId) changed.Add(nameof(UserSettings.DefaultTogglProjectId));
        if (previous.AiEnabled != updated.AiEnabled) changed.Add(nameof(UserSettings.AiEnabled));
        if (previous.AutoSyncEnabled != updated.AutoSyncEnabled) changed.Add(nameof(UserSettings.AutoSyncEnabled));
        if (previous.SyncIntervalMinutes != updated.SyncIntervalMinutes) changed.Add(nameof(UserSettings.SyncIntervalMinutes));
        if (previous.SlackTitle != updated.SlackTitle) changed.Add(nameof(UserSettings.SlackTitle));
        if (previous.SlackTaskHeading != updated.SlackTaskHeading) changed.Add(nameof(UserSettings.SlackTaskHeading));
        if (!previous.SlackExtraLines.SequenceEqual(updated.SlackExtraLines)) changed.Add(nameof(UserSettings.SlackExtraLines));
        return changed.Count > 0 ? string.Join(", ", changed) : "(no fields changed)";
    }

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
