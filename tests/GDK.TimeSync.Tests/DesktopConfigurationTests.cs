using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Desktop.ViewModels;
using System.Text.Json;

namespace GDK.TimeSync.Tests;

public sealed class DesktopConfigurationTests
{
    [Fact]
    public void Settings_json_contains_only_persisted_non_secret_fields()
    {
        var json = JsonSerializer.Serialize(new UserSettings
        {
            JiraBaseUrl = "https://jira.cgm.ag",
            TogglWorkspaceId = 42,
            ReviewReminderTime = "16:30",
            DefaultTempoWorkCategory = "DEVELOPMENT",
            AiEnabled = true
        });

        Assert.DoesNotContain("IsConfigured", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PersonalAccess", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TogglWorkspaceId", json, StringComparison.Ordinal);
        Assert.Contains("ReviewReminderTime", json, StringComparison.Ordinal);
        Assert.Contains("DefaultTempoWorkCategory", json, StringComparison.Ordinal);
        Assert.Contains("AiEnabled", json, StringComparison.Ordinal);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("webhook", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("incoming webhook", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Slack_presentation_preferences_round_trip_without_serializing_credentials()
    {
        var preferences = new UserSettings
        {
            JiraBaseUrl = "https://jira.cgm.ag",
            SlackTitle = "Daily delivery",
            SlackTaskHeading = "Completed work",
            SlackExtraLines = ["Thank you, team."]
        };
        var json = JsonSerializer.Serialize(preferences);
        var settings = new FakeSettingsStore(new UserSettings { JiraBaseUrl = "https://jira.cgm.ag" });
        var viewModel = new SettingsViewModel(new FakeCredentialStore(), settings, new ConfigurationStateService(new FakeCredentialStore(), settings));

        await viewModel.SaveAsync(preferences, null, null, null);

        Assert.Equal("Daily delivery", settings.Current.SlackTitle);
        Assert.Equal("Completed work", settings.Current.SlackTaskHeading);
        Assert.Equal(["Thank you, team."], settings.Current.SlackExtraLines);
        Assert.DoesNotContain("webhook", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task View_model_save_rejects_sensitive_slack_presentation_text()
    {
        var settings = new FakeSettingsStore(new UserSettings { JiraBaseUrl = "https://jira.cgm.ag" });
        var viewModel = new SettingsViewModel(new FakeCredentialStore(), settings, new ConfigurationStateService(new FakeCredentialStore(), settings));
        const string sentinel = "https://hooks.slack.com/services/T000/B000/sentinel-webhook";

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => viewModel.SaveAsync(new UserSettings
        {
            JiraBaseUrl = "https://jira.cgm.ag",
            SlackTitle = sentinel
        }, null, null, null));

        Assert.Equal("Slack presentation preferences must not contain sensitive content.", exception.Message);
        Assert.DoesNotContain(sentinel, JsonSerializer.Serialize(settings.Current), StringComparison.Ordinal);
    }

    [Fact]
    public void Jira_user_round_trips_as_a_non_secret_json_setting()
    {
        const string jiraUser = "planner@example.com";

        var json = JsonSerializer.Serialize(new UserSettings { JiraUser = jiraUser });
        var restored = JsonSerializer.Deserialize<UserSettings>(json);

        Assert.Equal(jiraUser, restored!.JiraUser);
        Assert.Contains("JiraUser", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PersonalAccess", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("webhook", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Saving_slack_webhook_records_only_its_credential_key_and_exposes_presence()
    {
        const string webhook = "https://hooks.slack.com/services/sentinel-webhook";
        var credentials = new FakeCredentialStore();
        var settings = new FakeSettingsStore(new UserSettings { JiraBaseUrl = "https://jira.cgm.ag" });
        var state = new ConfigurationStateService(credentials, settings);
        var viewModel = new SettingsViewModel(credentials, settings, state);

        await viewModel.SaveAsync("https://jira.cgm.ag", null, null, webhook);

        Assert.Equal([CredentialKeys.SlackWebhook], credentials.SavedKeys);
        Assert.True(credentials.WasSaved(CredentialKeys.SlackWebhook, webhook));
        Assert.True(viewModel.IsSlackWebhookConfigured);
        Assert.DoesNotContain(
            typeof(SettingsViewModel).GetProperties(),
            property => property.PropertyType == typeof(string) && Equals(property.GetValue(viewModel), webhook));

        var storedJson = JsonSerializer.Serialize(settings.Current);
        Assert.DoesNotContain(webhook, storedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("webhook", storedJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", storedJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Saving_a_draft_that_fails_does_not_replace_current_view_model_preferences()
    {
        var initial = new UserSettings { JiraBaseUrl = "https://jira.cgm.ag", JiraUser = "saved@example.com", ReviewReminderTime = "16:00" };
        var credentials = new FakeCredentialStore();
        var settings = new ThrowingSettingsStore(initial);
        var state = new ConfigurationStateService(credentials, settings);
        var viewModel = new SettingsViewModel(credentials, settings, state);
        await viewModel.LoadAsync();

        await Assert.ThrowsAsync<SettingsSaveException>(() => viewModel.SaveAsync(initial with { JiraUser = "draft@example.com", ReviewReminderTime = "17:00" }, null, null, null));

        Assert.Equal("16:00", viewModel.ReviewReminderTime);
        Assert.Equal("saved@example.com", viewModel.JiraUser);
    }

    [Fact]
    public async Task Invalid_jira_user_prevents_saving_credentials_or_settings()
    {
        var credentials = new FakeCredentialStore();
        var initial = new UserSettings { JiraBaseUrl = "https://jira.cgm.ag", JiraUser = "saved@example.com" };
        var settings = new FakeSettingsStore(initial);
        var state = new ConfigurationStateService(credentials, settings);
        var viewModel = new SettingsViewModel(credentials, settings, state);

        await Assert.ThrowsAsync<ArgumentException>(() => viewModel.SaveAsync(initial with { JiraUser = "not-an-email" }, "toggl-token", null, null));

        Assert.Empty(credentials.SavedKeys);
        Assert.Equal(initial, settings.Current);
    }

    [Fact]
    public async Task Saving_a_valid_jira_user_persists_the_normalized_value()
    {
        var credentials = new FakeCredentialStore();
        var settings = new FakeSettingsStore(new UserSettings { JiraBaseUrl = "https://jira.cgm.ag" });
        var state = new ConfigurationStateService(credentials, settings);
        var viewModel = new SettingsViewModel(credentials, settings, state);

        await viewModel.SaveAsync(new UserSettings { JiraBaseUrl = "https://jira.cgm.ag", JiraUser = "  planner@example.com  " }, null, null, null);

        Assert.Equal("planner@example.com", settings.Current.JiraUser);
        Assert.Equal("planner@example.com", viewModel.JiraUser);
    }

    [Fact]
    public async Task Saving_non_secret_preferences_persists_validated_values()
    {
        var credentials = new FakeCredentialStore();
        var settings = new FakeSettingsStore(new UserSettings { JiraBaseUrl = "https://jira.cgm.ag" });
        var state = new ConfigurationStateService(credentials, settings);
        var viewModel = new SettingsViewModel(credentials, settings, state)
        {
            ReviewReminderTime = "16:30",
            DefaultTempoWorkCategory = "SUPPORT",
            TogglWorkspaceId = 42,
            AiEnabled = true
        };

        await viewModel.SaveAsync("https://jira.cgm.ag", null, null);

        Assert.Equal("16:30", settings.Current.ReviewReminderTime);
        Assert.Equal("SUPPORT", settings.Current.DefaultTempoWorkCategory);
        Assert.Equal(42, settings.Current.TogglWorkspaceId);
        Assert.True(settings.Current.AiEnabled);
    }

    [Fact]
    public async Task Invalid_review_time_prevents_saving_credentials_or_settings()
    {
        var credentials = new FakeCredentialStore();
        var initial = new UserSettings { JiraBaseUrl = "https://jira.cgm.ag", ReviewReminderTime = "16:00" };
        var settings = new FakeSettingsStore(initial);
        var state = new ConfigurationStateService(credentials, settings);
        var viewModel = new SettingsViewModel(credentials, settings, state) { ReviewReminderTime = "4:30 pm" };

        await Assert.ThrowsAsync<ArgumentException>(() => viewModel.SaveAsync("https://jira.cgm.ag", "toggl-token", null));

        Assert.Empty(credentials.SavedKeys);
        Assert.Equal(initial, settings.Current);
    }
    [Fact]
    public async Task Saving_tokens_uses_canonical_credential_keys_and_refreshes_configuration()
    {
        const string togglToken = "toggl-token";
        const string jiraPat = "jira-token";
        const string slackWebhook = "https://hooks.slack.com/services/sentinel-webhook";
        var credentials = new FakeCredentialStore();
        var settings = new FakeSettingsStore(new UserSettings { JiraBaseUrl = "https://jira.cgm.ag" });
        var state = new ConfigurationStateService(credentials, settings);
        var viewModel = new SettingsViewModel(credentials, settings, state);
        var refreshEvents = 0;
        state.ConfigurationChanged += (_, _) => refreshEvents++;

        await viewModel.SaveAsync("https://jira.cgm.ag", togglToken, jiraPat, slackWebhook);

        Assert.Contains(CredentialKeys.TogglApiToken, credentials.SavedKeys);
        Assert.Contains(CredentialKeys.JiraPat, credentials.SavedKeys);
        Assert.True(state.IsConfigured);
        Assert.True(refreshEvents > 0);
        Assert.DoesNotContain(typeof(SettingsViewModel).GetProperties(), property =>
            property.PropertyType == typeof(string) &&
            (Equals(property.GetValue(viewModel), togglToken) || Equals(property.GetValue(viewModel), jiraPat) || Equals(property.GetValue(viewModel), slackWebhook)));

        var storedJson = JsonSerializer.Serialize(settings.Current);
        Assert.DoesNotContain(togglToken, storedJson, StringComparison.Ordinal);
        Assert.DoesNotContain(jiraPat, storedJson, StringComparison.Ordinal);
        Assert.DoesNotContain(slackWebhook, storedJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Opening_settings_reports_existing_credentials_without_exposing_secrets()
    {
        var credentials = new FakeCredentialStore(CredentialKeys.TogglApiToken, CredentialKeys.JiraPat);
        var settings = new FakeSettingsStore(new UserSettings { JiraBaseUrl = "https://jira.cgm.ag" });
        var state = new ConfigurationStateService(credentials, settings);
        var viewModel = new SettingsViewModel(credentials, settings, state);

        await viewModel.LoadAsync();

        Assert.True(viewModel.IsTogglTokenConfigured);
        Assert.True(viewModel.IsJiraPatConfigured);
        Assert.DoesNotContain(typeof(SettingsViewModel).GetProperties(), property => property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase) && property.PropertyType == typeof(string));
    }

    [Fact]
    public async Task Settings_save_failure_reports_that_credentials_may_already_have_been_saved()
    {
        var credentials = new FakeCredentialStore();
        var settings = new ThrowingSettingsStore();
        var state = new ConfigurationStateService(credentials, settings);
        var viewModel = new SettingsViewModel(credentials, settings, state);

        var exception = await Assert.ThrowsAsync<SettingsSaveException>(() => viewModel.SaveAsync("https://jira.cgm.ag", "toggl-token", "jira-token"));

        Assert.Contains("may have been saved", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(CredentialKeys.TogglApiToken, credentials.SavedKeys);
    }
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public async Task Configuration_requires_both_credentials_and_a_valid_jira_url(bool toggl, bool jira, bool expected)
    {
        var keys = new List<string>();
        if (toggl) keys.Add(CredentialKeys.TogglApiToken);
        if (jira) keys.Add(CredentialKeys.JiraPat);
        var settings = new FakeSettingsStore(new UserSettings { JiraBaseUrl = "https://jira.cgm.ag" });
        var state = new ConfigurationStateService(new FakeCredentialStore(keys.ToArray()), settings);

        await state.RefreshAsync();

        Assert.Equal(expected, state.IsConfigured);
    }

    [Fact]
    public async Task Invalid_jira_url_prevents_configuration()
    {
        var state = new ConfigurationStateService(new FakeCredentialStore(CredentialKeys.TogglApiToken, CredentialKeys.JiraPat), new FakeSettingsStore(new UserSettings { JiraBaseUrl = "not a url" }));

        await state.RefreshAsync();

        Assert.False(state.IsConfigured);
    }

    [Fact]
    public async Task Sync_now_enablement_tracks_configuration_and_synchronization_state()
    {
        var state = new ConfigurationStateService(
            new FakeCredentialStore(CredentialKeys.TogglApiToken, CredentialKeys.JiraPat),
            new FakeSettingsStore(new UserSettings { JiraBaseUrl = "https://jira.cgm.ag" }));
        var viewModel = new MainViewModel(state);

        await viewModel.InitializeAsync();
        Assert.True(viewModel.SyncNowCommand.CanExecute(null));

        viewModel.IsSynchronizing = true;
        Assert.False(viewModel.SyncNowCommand.CanExecute(null));

        viewModel.IsSynchronizing = false;
        Assert.True(viewModel.SyncNowCommand.CanExecute(null));
    }

    private sealed class FakeCredentialStore(params string[] existingKeys) : ICredentialStore
    {
        private readonly HashSet<string> keys = [.. existingKeys];

        public List<string> SavedKeys { get; } = [];

        public Task SaveAsync(string key, string secret, CancellationToken cancellationToken = default)
        {
            keys.Add(key);
            SavedKeys.Add(key);
            savedSecrets[key] = secret;
            return Task.CompletedTask;
        }

        private readonly Dictionary<string, string> savedSecrets = [];

        public bool WasSaved(string key, string secret) => savedSecrets.TryGetValue(key, out var value) && value == secret;

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(keys.Contains(key));

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            keys.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSettingsStore(UserSettings initial) : IUserSettingsStore
    {
        public UserSettings Current { get; private set; } = initial;

        public UserSettings Load() => Current;

        public void Save(UserSettings settings) => Current = settings;
    }
    private sealed class ThrowingSettingsStore(UserSettings? initial = null) : IUserSettingsStore
    {
        private readonly UserSettings current = initial ?? new UserSettings { JiraBaseUrl = "https://jira.cgm.ag" };

        public UserSettings Load() => current;

        public void Save(UserSettings settings) => throw new IOException("Test-only write failure.");
    }
}
