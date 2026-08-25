using GDK.TimeSync.Desktop.ViewModels;

namespace GDK.TimeSync.Desktop.Services;

public sealed class TogglAutoSyncService(MainViewModel mainViewModel, IUserSettingsStore settingsStore, TimeProvider timeProvider) : ITogglAutoSyncService
{
    private readonly object syncRoot = new();
    private DateTimeOffset? lastSyncedAt;
    private CancellationTokenSource? timerCancellation;
    private Task? timerLoop;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            if (timerLoop is not null) return Task.CompletedTask;

            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timerCancellation = cancellation;
            timerLoop = RunTimerAsync(cancellation.Token);
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

    internal async Task<bool> CheckNowAsync()
    {
        var settings = settingsStore.Load();
        if (!settings.AutoSyncEnabled) return false;

        var intervalMinutes = Math.Max(1, settings.SyncIntervalMinutes);
        var now = timeProvider.GetUtcNow();
        lock (syncRoot)
        {
            if (lastSyncedAt is { } last && now - last < TimeSpan.FromMinutes(intervalMinutes)) return false;
            lastSyncedAt = now;
        }

        try
        {
            await mainViewModel.SyncNowAsync().ConfigureAwait(false);
        }
        catch
        {
            // MainViewModel.SyncNowAsync already reports failure via SyncStatusText; a background
            // loop must never die from an unexpected fault here.
        }

        return true;
    }

    private async Task RunTimerAsync(CancellationToken cancellationToken)
    {
        await CheckNowAsync().ConfigureAwait(false);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                await CheckNowAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}
