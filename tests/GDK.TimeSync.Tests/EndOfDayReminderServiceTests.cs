using GDK.TimeSync.Desktop.Services;

namespace GDK.TimeSync.Tests;

public sealed class EndOfDayReminderServiceTests
{
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
        var service = CreateService("not-a-time", EndOfDayReminderMode.Both, new FakeTimeProvider(new DateTimeOffset(2026, 8, 14, 15, 59, 0, TimeSpan.Zero)));

        Assert.False(service.CheckNow());
    }

    private static EndOfDayReminderService CreateService(string time, EndOfDayReminderMode mode, TimeProvider clock) =>
        new(new FakeSettingsStore(new UserSettings { ReviewReminderTime = time, EndOfDayReminderMode = mode }), clock);

    private sealed class FakeSettingsStore(UserSettings settings) : IUserSettingsStore
    {
        public UserSettings Load() => settings;

        public void Save(UserSettings value) { }
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void Advance(TimeSpan amount) => current += amount;
    }
}
