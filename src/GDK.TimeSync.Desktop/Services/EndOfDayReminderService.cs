using System.Globalization;

namespace GDK.TimeSync.Desktop.Services;

public sealed class EndOfDayReminderService(IUserSettingsStore settingsStore, TimeProvider timeProvider) : IEndOfDayReminderService
{
    private readonly object syncRoot = new();
    private DateOnly? lastRaisedDate;
    private CancellationTokenSource? timerCancellation;
    private Task? timerLoop;

    public event EventHandler<ReviewDueEventArgs>? ReviewDue;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            if (timerLoop is not null) return Task.CompletedTask;

            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timerCancellation = cancellation;
            timerLoop = RunTimerAsync(cancellation.Token);
            CheckNow();
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? cancellation;
        Task? loop;
        lock (syncRoot)
        {
            cancellation = timerCancellation;
            loop = timerLoop;
            timerCancellation = null;
            timerLoop = null;
        }

        if (cancellation is null || loop is null) return;

        cancellation.Cancel();
        try
        {
            await loop.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    internal bool CheckNow()
    {
        var now = timeProvider.GetLocalNow();
        var settings = settingsStore.Load();
        var date = DateOnly.FromDateTime(now.DateTime);

        lock (syncRoot)
        {
            if (now.TimeOfDay < ParseTimeOrDefault(settings.ReviewReminderTime).ToTimeSpan() || lastRaisedDate == date) return false;

            lastRaisedDate = date;
        }

        ReviewDue?.Invoke(this, new ReviewDueEventArgs(EndOfDayReminderModes.Normalize(settings.EndOfDayReminderMode)));
        return true;
    }

    private async Task RunTimerAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) CheckNow();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private static TimeOnly ParseTimeOrDefault(string value) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : new TimeOnly(16, 0);
}
