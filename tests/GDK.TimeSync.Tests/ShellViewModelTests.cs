using GDK.TimeSync.Core;
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

    [Fact]
    public async Task Navigating_to_review_awaits_one_local_snapshot_refresh_without_external_effects()
    {
        var credentials = new FakeCredentialStore();
        var date = new DateOnly(2026, 8, 13);
        var item = PlannedWorkItem.Create(date, "Work", "CGM-1", "Completed", TimeSpan.FromMinutes(30));
        var snapshot = new TrackingPlanSnapshotProvider(DailyPlan.Create(date, [item]));
        var review = new ReviewViewModel(snapshot);
        var viewModel = new ShellViewModel(new ConfigurationStateService(credentials, new FakeSettingsStore()), review: review);

        await viewModel.NavigateAsync(NavigationPage.Review);

        Assert.Equal(NavigationPage.Review, viewModel.SelectedPage);
        Assert.Equal([item.Id], review.Items.Select(value => value.Id));
        Assert.Equal(1, snapshot.Reads);
        Assert.Equal(0, credentials.GetCalls);
    }

    private sealed class FakeCredentialStore : ICredentialStore
    {
        public int GetCalls { get; private set; }
        public Task SaveAsync(string key, string secret, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) { GetCalls++; return Task.FromResult<string?>(null); }
        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TrackingPlanSnapshotProvider(DailyPlan plan) : ILocalPlanSnapshotProvider
    {
        public int Reads { get; private set; }
        public DailyPlan GetSnapshot() { Reads++; return plan; }
    }

    private sealed class FakeSettingsStore : IUserSettingsStore
    {
        public UserSettings Load() => new();
        public void Save(UserSettings settings) { }
    }
}
