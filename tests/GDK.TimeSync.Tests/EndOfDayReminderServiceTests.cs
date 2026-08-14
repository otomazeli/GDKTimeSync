using GDK.TimeSync.Desktop.Services;

namespace GDK.TimeSync.Tests;

public sealed class EndOfDayReminderServiceTests
{
    [Fact]
    public async Task Direct_exit_begins_stopping_without_waiting_for_the_timer_loop()
    {
        var reminder = new IncompleteStopReminderService();
        EventHandler<ReviewDueEventArgs> handler = (_, _) => { };
        reminder.ReviewDue += handler;

        await Task.Run(() => ReminderLifecycle.BeginStop(reminder, handler)).WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(1, reminder.StopCalls);
        Assert.Equal(0, reminder.SubscriptionCount);
    }

    [Fact]
    public async Task Stopping_a_faulting_reminder_still_runs_the_remaining_exit_action()
    {
        var reminder = new ThrowingStopReminderService();
        EventHandler<ReviewDueEventArgs> handler = (_, _) => { };
        reminder.ReviewDue += handler;
        var remainingExitActions = 0;

        await ReminderLifecycle.StopThenAsync(reminder, handler, () =>
        {
            remainingExitActions++;
            return Task.CompletedTask;
        });

        Assert.Equal(1, reminder.StopCalls);
        Assert.Equal(0, reminder.SubscriptionCount);
        Assert.Equal(1, remainingExitActions);
    }

    [Fact]
    public void CheckNow_Raises_once_after_the_configured_time_for_each_local_date()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 14, 15, 59, 0, TimeSpan.Zero));
        var settings = new FakeSettingsStore(new UserSettings { ReviewReminderTime = "16:00" });
        var service = new EndOfDayReminderService(settings, clock);
        var raised = 0;
        service.ReviewDue += (_, _) => raised++;

        Assert.False(service.CheckNow());
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(service.CheckNow());
        Assert.False(service.CheckNow());
        Assert.Equal(1, raised);
    }

    [Fact]
    public void CheckNow_Raises_again_on_the_next_local_date()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 14, 16, 0, 0, TimeSpan.Zero));
        var service = CreateService("16:00", EndOfDayReminderMode.Both, clock);

        Assert.True(service.CheckNow());
        clock.Advance(TimeSpan.FromDays(1));

        Assert.True(service.CheckNow());
    }

    [Fact]
    public void CheckNow_uses_both_for_an_invalid_persisted_mode()
    {
        var service = CreateService("16:00", (EndOfDayReminderMode)999, new FakeTimeProvider(new DateTimeOffset(2026, 8, 14, 16, 0, 0, TimeSpan.Zero)));
        EndOfDayReminderMode? mode = null;
        service.ReviewDue += (_, args) => mode = args.Mode;

        service.CheckNow();

        Assert.Equal(EndOfDayReminderMode.Both, mode);
    }

    [Fact]
    public void CheckNow_uses_1600_for_an_invalid_persisted_time()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 14, 15, 59, 0, TimeSpan.Zero));
        var service = CreateService("not-a-time", EndOfDayReminderMode.Both, clock);
        var raised = 0;
        service.ReviewDue += (_, _) => raised++;

        Assert.False(service.CheckNow());
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(service.CheckNow());
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task StartAsync_stops_the_timer_when_a_due_handler_stops_the_service()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 14, 16, 0, 0, TimeSpan.Zero));
        var service = CreateService("16:00", EndOfDayReminderMode.Both, clock);
        var raised = 0;
        service.ReviewDue += (_, _) =>
        {
            raised++;
            service.StopAsync().GetAwaiter().GetResult();
        };

        await service.StartAsync();

        Assert.Equal(1, raised);
        Assert.True(clock.TimerDisposed);
        clock.Advance(TimeSpan.FromDays(1));
        clock.Tick();
        await Task.Yield();
        Assert.Equal(1, raised);
    }

    private static EndOfDayReminderService CreateService(string time, EndOfDayReminderMode mode, TimeProvider clock) =>
        new(new FakeSettingsStore(new UserSettings { ReviewReminderTime = time, EndOfDayReminderMode = mode }), clock);

    private sealed class FakeSettingsStore(UserSettings settings) : IUserSettingsStore
    {
        public UserSettings Load() => settings;

        public void Save(UserSettings value) { }
    }

    private sealed class ThrowingStopReminderService : IEndOfDayReminderService
    {
        private EventHandler<ReviewDueEventArgs>? reviewDue;

        public int StopCalls { get; private set; }
        public int SubscriptionCount { get; private set; }

        public event EventHandler<ReviewDueEventArgs>? ReviewDue
        {
            add
            {
                reviewDue += value;
                SubscriptionCount++;
            }
            remove
            {
                reviewDue -= value;
                SubscriptionCount--;
            }
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCalls++;
            return Task.FromException(new InvalidOperationException("Test-only stop failure."));
        }
    }

    private sealed class IncompleteStopReminderService : IEndOfDayReminderService
    {
        private EventHandler<ReviewDueEventArgs>? reviewDue;
        private readonly TaskCompletionSource stopCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int StopCalls { get; private set; }
        public int SubscriptionCount { get; private set; }

        public event EventHandler<ReviewDueEventArgs>? ReviewDue
        {
            add { reviewDue += value; SubscriptionCount++; }
            remove { reviewDue -= value; SubscriptionCount--; }
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) { StopCalls++; return stopCompletion.Task; }
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        private FakeTimer? timer;

        public override DateTimeOffset GetUtcNow() => current.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void Advance(TimeSpan amount) => current += amount;

        public bool TimerDisposed => timer?.Disposed ?? false;

        public void Tick() => timer?.Tick();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            timer = new FakeTimer(callback, state);

        private sealed class FakeTimer(TimerCallback callback, object? state) : ITimer
        {
            public bool Disposed { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period) => !Disposed;

            public void Dispose() => Disposed = true;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void Tick()
            {
                if (!Disposed) callback(state);
            }
        }
    }
}
