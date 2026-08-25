using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Desktop.ViewModels;

namespace GDK.TimeSync.Tests;

public sealed class TogglAutoSyncServiceTests
{
    [Fact]
    public async Task StartAsync_TriggersAnImmediateSyncBeforeAnyTimerTick()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.Zero));
        var sync = new FakeTogglSyncService();
        var main = CreateMainViewModel(sync);
        var service = new TogglAutoSyncService(main, new FixedSettingsStore(new UserSettings { AutoSyncEnabled = true, SyncIntervalMinutes = 5 }), clock);

        await service.StartAsync();
        await sync.WaitForCallAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, sync.CallCount);
    }

    [Fact]
    public async Task Ticks_BeforeTheIntervalElapsedDoNotTriggerAnotherSync()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.Zero));
        var sync = new FakeTogglSyncService();
        var main = CreateMainViewModel(sync);
        var service = new TogglAutoSyncService(main, new FixedSettingsStore(new UserSettings { AutoSyncEnabled = true, SyncIntervalMinutes = 5 }), clock);

        await service.StartAsync();
        await sync.WaitForCallAsync(TimeSpan.FromSeconds(5));

        clock.Advance(TimeSpan.FromMinutes(4));
        clock.Tick();
        await Task.Delay(50);

        Assert.Equal(1, sync.CallCount);
    }

    [Fact]
    public async Task Ticks_AfterTheIntervalElapsedTriggerAnotherSync()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.Zero));
        var sync = new FakeTogglSyncService();
        var main = CreateMainViewModel(sync);
        var service = new TogglAutoSyncService(main, new FixedSettingsStore(new UserSettings { AutoSyncEnabled = true, SyncIntervalMinutes = 5 }), clock);

        await service.StartAsync();
        await sync.WaitForCallAsync(TimeSpan.FromSeconds(5));

        clock.Advance(TimeSpan.FromMinutes(5));
        clock.Tick();
        await sync.WaitForCallAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, sync.CallCount);
    }

    [Fact]
    public async Task CheckNowAsync_DoesNothingWhenAutoSyncIsDisabled()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.Zero));
        var sync = new FakeTogglSyncService();
        var main = CreateMainViewModel(sync);
        var service = new TogglAutoSyncService(main, new FixedSettingsStore(new UserSettings { AutoSyncEnabled = false }), clock);

        var fired = await service.CheckNowAsync();

        Assert.False(fired);
        Assert.Equal(0, sync.CallCount);
    }

    [Fact]
    public async Task StopAsync_PreventsFurtherSyncsFromLaterTicks()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.Zero));
        var sync = new FakeTogglSyncService();
        var main = CreateMainViewModel(sync);
        var service = new TogglAutoSyncService(main, new FixedSettingsStore(new UserSettings { AutoSyncEnabled = true, SyncIntervalMinutes = 5 }), clock);

        await service.StartAsync();
        await sync.WaitForCallAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync();

        clock.Advance(TimeSpan.FromMinutes(10));
        clock.Tick();
        await Task.Delay(50);

        Assert.Equal(1, sync.CallCount);
    }

    [Fact]
    public async Task ATransientSyncFailureDoesNotStopFutureTicks()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.Zero));
        var sync = new FakeTogglSyncService(failFirstCall: true);
        var main = CreateMainViewModel(sync);
        var service = new TogglAutoSyncService(main, new FixedSettingsStore(new UserSettings { AutoSyncEnabled = true, SyncIntervalMinutes = 5 }), clock);

        await service.StartAsync();
        await sync.WaitForCallAsync(TimeSpan.FromSeconds(5));

        clock.Advance(TimeSpan.FromMinutes(5));
        clock.Tick();
        await sync.WaitForCallAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, sync.CallCount);
    }

    private static MainViewModel CreateMainViewModel(ITogglSyncService sync) =>
        new(new FixedConfigurationStateService(isConfigured: true), sync, new TodayViewModel(date: new DateOnly(2026, 8, 24)));

    private sealed class FixedConfigurationStateService(bool isConfigured) : IConfigurationStateService
    {
        public bool IsConfigured => isConfigured;
        public bool HasTogglCredential => isConfigured;
        public bool HasJiraCredential => isConfigured;
        public event EventHandler? ConfigurationChanged { add { } remove { } }
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedSettingsStore(UserSettings settings) : IUserSettingsStore
    {
        public UserSettings Load() => settings;
        public void Save(UserSettings value) { }
    }

    private sealed class FakeTogglSyncService(bool failFirstCall = false) : ITogglSyncService
    {
        private readonly SemaphoreSlim signal = new(0);
        private bool hasFailedOnce;

        public int CallCount { get; private set; }

        public async Task WaitForCallAsync(TimeSpan timeout) => Assert.True(await signal.WaitAsync(timeout));

        public Task<TogglSyncPullResult> PullAsync(DateOnly date, IReadOnlyList<PlannedWorkItem> localItems, CancellationToken cancellationToken = default)
        {
            CallCount++;
            signal.Release();
            if (failFirstCall && !hasFailedOnce)
            {
                hasFailedOnce = true;
                throw new InvalidOperationException("Test-only transient failure.");
            }

            return Task.FromResult(TogglSyncPullResult.Empty());
        }
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        private FakeTimer? timer;

        public override DateTimeOffset GetUtcNow() => current.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void Advance(TimeSpan amount) => current += amount;

        public void Tick() => timer?.Tick();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            timer = new FakeTimer(callback, state);

        private sealed class FakeTimer(TimerCallback callback, object? state) : ITimer
        {
            private bool disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period) => !disposed;

            public void Dispose() => disposed = true;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void Tick()
            {
                if (!disposed) callback(state);
            }
        }
    }
}
