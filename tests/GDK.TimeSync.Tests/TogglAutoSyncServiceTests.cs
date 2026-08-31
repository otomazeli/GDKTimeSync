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
        var (main, today) = CreateMainViewModel(sync, new DateOnly(2026, 8, 24));
        var service = new TogglAutoSyncService(main, today, sync, new FakePlanRepository(), new FixedSettingsStore(new UserSettings { AutoSyncEnabled = true, SyncIntervalMinutes = 5 }), clock);

        await service.StartAsync();
        await sync.WaitForCallAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, sync.CallCount);
    }

    [Fact]
    public async Task Ticks_BeforeTheIntervalElapsedDoNotTriggerAnotherSync()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.Zero));
        var sync = new FakeTogglSyncService();
        var (main, today) = CreateMainViewModel(sync, new DateOnly(2026, 8, 24));
        var service = new TogglAutoSyncService(main, today, sync, new FakePlanRepository(), new FixedSettingsStore(new UserSettings { AutoSyncEnabled = true, SyncIntervalMinutes = 5 }), clock);

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
        var (main, today) = CreateMainViewModel(sync, new DateOnly(2026, 8, 24));
        var service = new TogglAutoSyncService(main, today, sync, new FakePlanRepository(), new FixedSettingsStore(new UserSettings { AutoSyncEnabled = true, SyncIntervalMinutes = 5 }), clock);

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
        var (main, today) = CreateMainViewModel(sync, new DateOnly(2026, 8, 24));
        var service = new TogglAutoSyncService(main, today, sync, new FakePlanRepository(), new FixedSettingsStore(new UserSettings { AutoSyncEnabled = false }), clock);

        var fired = await service.CheckNowAsync();

        Assert.False(fired);
        Assert.Equal(0, sync.CallCount);
    }

    [Fact]
    public async Task StopAsync_PreventsFurtherSyncsFromLaterTicks()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.Zero));
        var sync = new FakeTogglSyncService();
        var (main, today) = CreateMainViewModel(sync, new DateOnly(2026, 8, 24));
        var service = new TogglAutoSyncService(main, today, sync, new FakePlanRepository(), new FixedSettingsStore(new UserSettings { AutoSyncEnabled = true, SyncIntervalMinutes = 5 }), clock);

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
        var (main, today) = CreateMainViewModel(sync, new DateOnly(2026, 8, 24));
        var service = new TogglAutoSyncService(main, today, sync, new FakePlanRepository(), new FixedSettingsStore(new UserSettings { AutoSyncEnabled = true, SyncIntervalMinutes = 5 }), clock);

        await service.StartAsync();
        await sync.WaitForCallAsync(TimeSpan.FromSeconds(5));

        clock.Advance(TimeSpan.FromMinutes(5));
        clock.Tick();
        await sync.WaitForCallAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, sync.CallCount);
    }

    [Fact]
    public async Task CheckNowAsync_WhenTodayIsShowingRealTodayGoesThroughMainViewModel()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.Zero));
        var sync = new FakeTogglSyncService();
        var (main, today) = CreateMainViewModel(sync, new DateOnly(2026, 8, 24));
        var planRepository = new FakePlanRepository();
        var service = new TogglAutoSyncService(main, today, sync, planRepository, new FixedSettingsStore(new UserSettings { AutoSyncEnabled = true, SyncIntervalMinutes = 5 }), clock);

        var fired = await service.CheckNowAsync();

        Assert.True(fired);
        Assert.Equal(1, sync.CallCount);
        Assert.Equal(0, planRepository.GetCalls);
        Assert.Empty(planRepository.SavedPlans);
    }

    [Fact]
    public async Task CheckNowAsync_WhenTodayIsShowingRealTodayRoutesTheSyncThroughTheInjectedUiThreadInvoker()
    {
        // TodayViewModel.Items is an ObservableCollection bound to the UI; mutating it off the UI
        // thread (which is what every timer tick after the first does -- see RunTimerAsync) throws
        // NotSupportedException in a real WPF app. This proves CheckNowAsync never calls
        // MainViewModel.SyncNowAsync directly on the calling thread/context -- it always goes
        // through the injected marshaling delegate, which is what App.xaml.cs wires to the real
        // WPF Dispatcher in production. A real Dispatcher pump isn't practical in this xunit
        // project (no message loop), so this substitutes a recording delegate and asserts it is
        // the thing that actually invokes SyncNowAsync.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.Zero));
        var sync = new FakeTogglSyncService();
        var (main, today) = CreateMainViewModel(sync, new DateOnly(2026, 8, 24));
        var invoker = new RecordingUiThreadInvoker();
        var service = new TogglAutoSyncService(main, today, sync, new FakePlanRepository(), new FixedSettingsStore(new UserSettings { AutoSyncEnabled = true, SyncIntervalMinutes = 5 }), clock, invoker.InvokeAsync);

        var fired = await service.CheckNowAsync();

        Assert.True(fired);
        Assert.Equal(1, invoker.CallCount);
        Assert.Equal(1, sync.CallCount);
    }

    [Fact]
    public async Task CheckNowAsync_WhenTodayIsShowingADifferentDateSyncsRealTodayDirectlyWithoutTouchingTodayItems()
    {
        var realToday = new DateOnly(2026, 8, 24);
        var pastDate = new DateOnly(2026, 8, 20);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.Zero));
        var existing = PlannedWorkItem.Create(realToday, "Existing", comment: "Existing");
        var updated = existing with { Comment = "Updated via sync" };
        var added = PlannedWorkItem.Create(realToday, "Imported from Toggl", comment: "Imported from Toggl");
        var mainSync = new FakeTogglSyncService();
        var (main, today) = CreateMainViewModel(mainSync, pastDate);
        var directSync = new FakeTogglSyncService(result: new TogglSyncPullResult([added], [updated], 0, null));
        var planRepository = new FakePlanRepository(DailyPlan.Create(realToday, [existing]));
        var invoker = new RecordingUiThreadInvoker();
        var service = new TogglAutoSyncService(main, today, directSync, planRepository, new FixedSettingsStore(new UserSettings { AutoSyncEnabled = true, SyncIntervalMinutes = 5 }), clock, invoker.InvokeAsync);

        var fired = await service.CheckNowAsync();

        Assert.True(fired);
        Assert.Equal(0, mainSync.CallCount);
        Assert.Equal(1, directSync.CallCount);
        // This branch never touches TodayViewModel.Items, so it must not go through the UI-thread
        // invoker either -- confirms the fix is scoped to only the branch that needs it.
        Assert.Equal(0, invoker.CallCount);
        var savedPlan = Assert.Single(planRepository.SavedPlans);
        Assert.Equal(realToday, savedPlan.Date);
        Assert.Equal(2, savedPlan.Items.Count);
        Assert.Contains(savedPlan.Items, item => item.Id == existing.Id && item.Comment == "Updated via sync");
        Assert.Contains(savedPlan.Items, item => item.Id == added.Id);
        Assert.Equal(pastDate, today.Date);
        Assert.DoesNotContain(today.Items, item => item.Description is "Existing" or "Imported from Toggl");
    }

    [Fact]
    public async Task CheckNowAsync_ForADifferentDateWithNoExistingPlanTreatsItAsEmpty()
    {
        var realToday = new DateOnly(2026, 8, 24);
        var pastDate = new DateOnly(2026, 8, 20);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.Zero));
        var added = PlannedWorkItem.Create(realToday, "Imported from Toggl", comment: "Imported from Toggl");
        var (main, today) = CreateMainViewModel(new FakeTogglSyncService(), pastDate);
        var directSync = new FakeTogglSyncService(result: new TogglSyncPullResult([added], [], 0, null));
        var planRepository = new FakePlanRepository(plan: null);
        var service = new TogglAutoSyncService(main, today, directSync, planRepository, new FixedSettingsStore(new UserSettings { AutoSyncEnabled = true, SyncIntervalMinutes = 5 }), clock);

        var fired = await service.CheckNowAsync();

        Assert.True(fired);
        var savedPlan = Assert.Single(planRepository.SavedPlans);
        Assert.Equal(realToday, savedPlan.Date);
        Assert.Equal(added.Id, Assert.Single(savedPlan.Items).Id);
    }

    [Fact]
    public async Task CheckNowAsync_ForADifferentDateRetriesTheMergeWhenAnotherWriterWonTheRace()
    {
        var realToday = new DateOnly(2026, 8, 24);
        var pastDate = new DateOnly(2026, 8, 20);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.Zero));
        var existing = PlannedWorkItem.Create(realToday, "Existing", comment: "Existing");
        var added = PlannedWorkItem.Create(realToday, "Imported from Toggl", comment: "Imported from Toggl");
        var concurrentEdit = PlannedWorkItem.Create(realToday, "Concurrent edit", comment: "Written by Today view while we were syncing");
        var (main, today) = CreateMainViewModel(new FakeTogglSyncService(), pastDate);
        var directSync = new FakeTogglSyncService(result: new TogglSyncPullResult([added], [], 0, null));
        var planRepository = new FakePlanRepository(DailyPlan.Create(realToday, [existing]))
        {
            FailSaveTimes = 1,
            OnConflict = _ => DailyPlan.Create(realToday, [existing, concurrentEdit]) with { Version = 1 }
        };
        var service = new TogglAutoSyncService(main, today, directSync, planRepository, new FixedSettingsStore(new UserSettings { AutoSyncEnabled = true, SyncIntervalMinutes = 5 }), clock);

        var fired = await service.CheckNowAsync();

        Assert.True(fired);
        var savedPlan = Assert.Single(planRepository.SavedPlans);
        Assert.Equal(3, savedPlan.Items.Count);
        Assert.Contains(savedPlan.Items, item => item.Id == existing.Id);
        Assert.Contains(savedPlan.Items, item => item.Id == added.Id);
        Assert.Contains(savedPlan.Items, item => item.Id == concurrentEdit.Id);
    }

    private static (MainViewModel Main, TodayViewModel Today) CreateMainViewModel(ITogglSyncService sync, DateOnly date)
    {
        var today = new TodayViewModel(date: date);
        var main = new MainViewModel(new FixedConfigurationStateService(isConfigured: true), sync, today);
        return (main, today);
    }

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

    private sealed class RecordingUiThreadInvoker
    {
        public int CallCount { get; private set; }

        public Task InvokeAsync(Func<Task> action)
        {
            CallCount++;
            return action();
        }
    }

    private sealed class FakePlanRepository(DailyPlan? plan = null) : IDailyPlanRepository
    {
        public int GetCalls { get; private set; }
        public List<DailyPlan> SavedPlans { get; } = [];
        public int FailSaveTimes { get; set; }
        public Func<DailyPlan, DailyPlan>? OnConflict { get; set; }

        public Task<DailyPlan?> GetAsync(DateOnly date, CancellationToken cancellationToken = default)
        {
            GetCalls++;
            return Task.FromResult(plan);
        }

        public Task SaveAsync(DailyPlan value, CancellationToken cancellationToken = default)
        {
            if (FailSaveTimes > 0)
            {
                FailSaveTimes--;
                if (OnConflict is not null) plan = OnConflict(value);
                throw new PlanConcurrencyException(value.Date);
            }

            plan = value with { Version = value.Version + 1 };
            SavedPlans.Add(value);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTogglSyncService(bool failFirstCall = false, TogglSyncPullResult? result = null) : ITogglSyncService
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

            return Task.FromResult(result ?? TogglSyncPullResult.Empty());
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
