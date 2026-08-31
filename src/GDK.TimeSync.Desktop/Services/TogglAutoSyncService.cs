using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.ViewModels;

namespace GDK.TimeSync.Desktop.Services;

public sealed class TogglAutoSyncService(
    MainViewModel mainViewModel,
    TodayViewModel today,
    ITogglSyncService syncService,
    IDailyPlanRepository planRepository,
    IUserSettingsStore settingsStore,
    TimeProvider timeProvider,
    Func<Func<Task>, Task>? uiThreadInvoker = null) : ITogglAutoSyncService
{
    // Every tick after the first resumes on a thread-pool thread (no SynchronizationContext),
    // but MainViewModel.SyncNowAsync mutates TodayViewModel.Items, an ObservableCollection bound
    // to the UI. That mutation must run on the UI thread. Production wires a real dispatcher in
    // App.xaml.cs; tests that don't need UI marshaling get a same-thread pass-through.
    private readonly Func<Func<Task>, Task> runOnUiThread = uiThreadInvoker ?? (action => action());
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

        var realToday = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        try
        {
            if (today.Date == realToday)
                await runOnUiThread(() => mainViewModel.SyncNowAsync()).ConfigureAwait(false);
            else
                await SyncDateDirectlyAsync(realToday).ConfigureAwait(false);
        }
        catch
        {
            // MainViewModel.SyncNowAsync already reports failure via SyncStatusText; a background
            // loop must never die from an unexpected fault here.
        }

        return true;
    }

    // The user may leave Today showing a past date to finish/post it. Auto-sync must still keep
    // real-today's Toggl entries up to date without going through TodayViewModel (which represents
    // a different date at that moment) -- that would corrupt the displayed date. TodayViewModel can
    // still be independently saving real-today's row at the same moment (e.g. right after the user
    // navigates back to it); SaveAsync's optimistic-concurrency check catches that, and we recompute
    // the merge against the latest state and retry rather than clobbering whichever side loses.
    private async Task SyncDateDirectlyAsync(DateOnly date)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var plan = await planRepository.GetAsync(date).ConfigureAwait(false) ?? DailyPlan.Create(date, []);
            var result = await syncService.PullAsync(date, plan.Items).ConfigureAwait(false);
            if (result.Error is not null || (result.ItemsToAdd.Count == 0 && result.ItemsToUpdate.Count == 0))
                return;

            var merged = plan.Items.ToList();
            foreach (var updated in result.ItemsToUpdate)
            {
                var index = merged.FindIndex(item => item.Id == updated.Id);
                if (index >= 0) merged[index] = updated;
            }
            merged.AddRange(result.ItemsToAdd);

            try
            {
                await planRepository.SaveAsync(plan with { Items = merged }).ConfigureAwait(false);
                return;
            }
            catch (PlanConcurrencyException)
            {
            }
        }
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
