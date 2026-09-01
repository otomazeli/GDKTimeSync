using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Desktop.ViewModels;

namespace GDK.TimeSync.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task SyncNowAsync_ReportsCountsOnlyAndNeverRawEntryContent()
    {
        var date = new DateOnly(2026, 8, 24);
        var today = new TodayViewModel(date: date);
        var secretLookingItem = PlannedWorkItem.Create(date, "ghp_supersecrettoken", comment: "confidential client name");
        var syncService = new FakeTogglSyncService(new TogglSyncPullResult([secretLookingItem], [], 1, null));
        var main = new MainViewModel(new FixedConfigurationStateService(isConfigured: true), syncService, today);

        await main.SyncNowAsync();

        Assert.Equal("Imported 1, updated 0, 1 needs review.", main.SyncStatusText);
        Assert.DoesNotContain("ghp_supersecrettoken", main.SyncStatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("confidential client name", main.SyncStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SyncNowAsync_ReportsAGenericFailureAndNeverTheRawError()
    {
        var today = new TodayViewModel(date: new DateOnly(2026, 8, 24));
        var syncService = new FakeTogglSyncService(TogglSyncPullResult.Empty("raw upstream failure detail"));
        var main = new MainViewModel(new FixedConfigurationStateService(isConfigured: true), syncService, today);

        await main.SyncNowAsync();

        Assert.Equal("Sync failed: Toggl is not reachable or not configured.", main.SyncStatusText);
        Assert.DoesNotContain("raw upstream failure detail", main.SyncStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SyncNowAsync_TogglesIsSynchronizingAndBlocksReentryWhileInFlight()
    {
        var today = new TodayViewModel(date: new DateOnly(2026, 8, 24));
        var gate = new TaskCompletionSource();
        var syncService = new FakeTogglSyncService(TogglSyncPullResult.Empty(), gate.Task);
        var configuration = new FixedConfigurationStateService(isConfigured: true);
        var main = new MainViewModel(configuration, syncService, today);

        var inFlight = main.SyncNowAsync();
        Assert.True(main.IsSynchronizing);
        Assert.False(main.SyncNowCommand.CanExecute(null));

        gate.SetResult();
        await inFlight;

        Assert.False(main.IsSynchronizing);
        Assert.True(main.SyncNowCommand.CanExecute(null));
        Assert.Equal(1, syncService.CallCount);
    }

    [Fact]
    public async Task SyncNowAsync_DoesNothingWithoutASyncServiceOrTodayViewModel()
    {
        var main = new MainViewModel(new FixedConfigurationStateService(isConfigured: true));

        await main.SyncNowAsync();

        Assert.Null(main.SyncStatusText);
        Assert.False(main.IsSynchronizing);
    }

    [Fact]
    public async Task SelectingAnotherDate_PullsThatDateFromTogglWithoutWaitingForTheAutoSyncInterval()
    {
        var today = new TodayViewModel(date: new DateOnly(2026, 8, 31));
        var syncService = new FakeTogglSyncService(TogglSyncPullResult.Empty());
        _ = new MainViewModel(new FixedConfigurationStateService(isConfigured: true), syncService, today);

        await today.SelectDateAsync(new DateOnly(2026, 9, 1));
        await syncService.WaitForCallAsync();

        Assert.Equal(1, syncService.CallCount);
        Assert.Equal(new DateOnly(2026, 9, 1), syncService.LastDate);
    }

    [Fact]
    public async Task GoToToday_PullsAgainEvenWhenTodayIsAlreadyTheSelectedDate()
    {
        var date = DateOnly.FromDateTime(DateTime.Today);
        var today = new TodayViewModel(date: date);
        var syncService = new FakeTogglSyncService(TogglSyncPullResult.Empty());
        _ = new MainViewModel(new FixedConfigurationStateService(isConfigured: true), syncService, today);

        today.GoToTodayCommand.Execute(null);
        await syncService.WaitForCallAsync();

        Assert.Equal(1, syncService.CallCount);
        Assert.Equal(date, syncService.LastDate);
    }

    [Fact]
    public async Task ShellExposesTheSyncResultSoTheWindowCanShowIt()
    {
        var today = new TodayViewModel(date: new DateOnly(2026, 8, 24));
        var syncService = new FakeTogglSyncService(TogglSyncPullResult.Empty());
        var main = new MainViewModel(new FixedConfigurationStateService(isConfigured: true), syncService, today);
        var shell = new ShellViewModel(new FixedConfigurationStateService(isConfigured: true), today, main: main);

        await main.SyncNowAsync();

        Assert.Same(main, shell.Main);
        Assert.Equal("Imported 0, updated 0, 0 needs review.", shell.Main!.SyncStatusText);
    }

    private sealed class FixedConfigurationStateService(bool isConfigured) : IConfigurationStateService
    {
        public bool IsConfigured => isConfigured;
        public bool HasTogglCredential => isConfigured;
        public bool HasJiraCredential => isConfigured;
        public event EventHandler? ConfigurationChanged { add { } remove { } }
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeTogglSyncService(TogglSyncPullResult result, Task? gate = null) : ITogglSyncService
    {
        private readonly TaskCompletionSource called = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }
        public DateOnly? LastDate { get; private set; }

        // Date selection kicks the sync off without awaiting it, the same way the Sync now command
        // does. Wait for the call rather than sleeping -- and give up rather than hang, so a
        // regression fails the assertion below instead of stalling the test host.
        public Task WaitForCallAsync() => Task.WhenAny(called.Task, Task.Delay(TimeSpan.FromSeconds(2)));

        public async Task<TogglSyncPullResult> PullAsync(DateOnly date, IReadOnlyList<PlannedWorkItem> localItems, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastDate = date;
            called.TrySetResult();
            if (gate is not null) await gate;
            return result;
        }
    }
}
