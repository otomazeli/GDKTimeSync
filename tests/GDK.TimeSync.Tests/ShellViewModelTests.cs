using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Desktop.ViewModels;

namespace GDK.TimeSync.Tests;

public sealed class ShellViewModelTests
{
    [Fact]
    public void NavigateCommand_SelectsRequestedPage()
    {
        var configurationState = new ConfigurationStateService(new FakeCredentialStore(), new FakeSettingsStore());
        var viewModel = new ShellViewModel(configurationState);

        viewModel.NavigateCommand.Execute(NavigationPage.Review);

        Assert.Equal(NavigationPage.Review, viewModel.SelectedPage);
    }

    private sealed class FakeCredentialStore : ICredentialStore
    {
        public Task SaveAsync(string key, string secret, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSettingsStore : IUserSettingsStore
    {
        public UserSettings Load() => new();
        public void Save(UserSettings settings) { }
    }
}
