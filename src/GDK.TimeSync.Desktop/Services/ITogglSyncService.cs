using GDK.TimeSync.Core;

namespace GDK.TimeSync.Desktop.Services;

public sealed record TogglSyncPullResult(
    IReadOnlyList<PlannedWorkItem> ItemsToAdd,
    IReadOnlyList<PlannedWorkItem> ItemsToUpdate,
    int ReconciliationFlaggedCount,
    string? Error)
{
    public static TogglSyncPullResult Empty(string? error = null) => new([], [], 0, error);
}

public interface ITogglSyncService
{
    Task<TogglSyncPullResult> PullAsync(DateOnly date, IReadOnlyList<PlannedWorkItem> localItems, CancellationToken cancellationToken = default);
}
