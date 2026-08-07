using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Desktop.ViewModels;
using System.Text.Json;

namespace GDK.TimeSync.Tests;

public sealed class DesktopConfigurationTests
{
    [Fact]
    public void Settings_json_contains_only_persisted_non_secret_fields()
    {
        var json = JsonSerializer.Serialize(new UserSettings { JiraBaseUrl = "https://jira.cgm.ag" });

        Assert.DoesNotContain("IsConfigured", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PersonalAccess", json, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task Saving_tokens_uses_canonical_credential_keys_and_refreshes_configuration()
    {
        var credentials = new FakeCredentialStore();
        var settings = new FakeSettingsStore(new UserSettings { JiraBaseUrl = "https://jira.cgm.ag" });
        var state = new ConfigurationStateService(credentials, settings);
        var viewModel = new SettingsViewModel(credentials, settings, state);
        var refreshEvents = 0;
        state.ConfigurationChanged += (_, _) => refreshEvents++;

        await viewModel.SaveAsync("https://jira.cgm.ag", "toggl-token", "jira-token");

        Assert.Contains(CredentialKeys.TogglApiToken, credentials.SavedKeys);
        Assert.Contains(CredentialKeys.JiraPat, credentials.SavedKeys);
        Assert.True(state.IsConfigured);
        Assert.True(refreshEvents > 0);
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
            return Task.CompletedTask;
        }

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
    private sealed class ThrowingSettingsStore : IUserSettingsStore
    {
        public UserSettings Load() => new() { JiraBaseUrl = "https://jira.cgm.ag" };

        public void Save(UserSettings settings) => throw new IOException("Test-only write failure.");
    }
}
