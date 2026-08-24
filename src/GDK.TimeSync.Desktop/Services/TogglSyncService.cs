using GDK.TimeSync.Core;
using GDK.TimeSync.Toggl;

namespace GDK.TimeSync.Desktop.Services;

public sealed class TogglSyncService(
    IIntegrationClientFactory clients,
    IUserSettingsStore settings,
    IDeliveryAttemptRepository attempts) : ITogglSyncService
{
    public async Task<TogglSyncPullResult> PullAsync(DateOnly date, IReadOnlyList<PlannedWorkItem> localItems, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localItems);

        long workspaceId;
        try
        {
            var configuration = settings.Load();
            if (configuration.TogglWorkspaceId is not > 0)
                return TogglSyncPullResult.Empty("Toggl is not configured.");
            workspaceId = configuration.TogglWorkspaceId.Value;
        }
        catch
        {
            return TogglSyncPullResult.Empty("Toggl is not configured.");
        }

        IReadOnlyList<TogglTimeEntry> entries;
        try
        {
            using var toggl = await clients.CreateTogglAsync(cancellationToken);
            entries = await toggl.GetTimeEntriesAsync(date, date, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return TogglSyncPullResult.Empty("Toggl is not reachable.");
        }

        var linkIndex = new Dictionary<long, PlannedWorkItem>();
        foreach (var item in localItems)
        {
            var linkId = item.TogglEntryId ?? await TryGetLinkedTogglEntryIdAsync(item.Id, cancellationToken);
            if (linkId is { } id && !linkIndex.ContainsKey(id))
                linkIndex[id] = item;
        }

        var itemsToAdd = new List<PlannedWorkItem>();
        var itemsToUpdate = new List<PlannedWorkItem>();
        var reconciliationCount = 0;

        foreach (var entry in entries)
        {
            if (entry.WorkspaceId != workspaceId || entry.Stop is not { } stop)
                continue;

            if (!linkIndex.TryGetValue(entry.Id, out var localItem))
            {
                itemsToAdd.Add(BuildImportedItem(date, entry, stop));
                continue;
            }

            var attempt = await TryGetAttemptAsync(localItem.Id, cancellationToken);
            var (entryStart, entryEnd) = ToLocalRange(entry.Start, stop);
            var changed = localItem.Start != entryStart || localItem.End != entryEnd ||
                          !string.Equals(localItem.Comment, entry.Description, StringComparison.Ordinal);

            if (attempt is { Status: DeliveryAttemptStatus.Succeeded })
            {
                if (changed && attempt.FailureCode != DeliveryFailureCode.RemoteChangedAfterDelivery)
                {
                    try
                    {
                        await attempts.SaveAsync(
                            attempt with { Status = DeliveryAttemptStatus.ReconciliationRequired, FailureCode = DeliveryFailureCode.RemoteChangedAfterDelivery },
                            CancellationToken.None);
                        reconciliationCount++;
                    }
                    catch
                    {
                        // Best-effort: if we cannot persist the flag, leave the succeeded record untouched
                        // rather than silently accepting the remote change.
                    }
                }

                continue;
            }

            if (!changed && localItem.TogglEntryId == entry.Id)
                continue;

            itemsToUpdate.Add(localItem with
            {
                Start = entryStart,
                End = entryEnd,
                Comment = entry.Description,
                Duration = stop - entry.Start,
                TogglEntryId = entry.Id
            });
        }

        return new TogglSyncPullResult(itemsToAdd, itemsToUpdate, reconciliationCount, null);
    }

    private async Task<long?> TryGetLinkedTogglEntryIdAsync(Guid plannedWorkItemId, CancellationToken cancellationToken) =>
        (await TryGetAttemptAsync(plannedWorkItemId, cancellationToken))?.TogglEntryId;

    private async Task<DeliveryAttempt?> TryGetAttemptAsync(Guid plannedWorkItemId, CancellationToken cancellationToken)
    {
        try
        {
            return await attempts.GetAsync(plannedWorkItemId, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static PlannedWorkItem BuildImportedItem(DateOnly date, TogglTimeEntry entry, DateTimeOffset stop)
    {
        var (start, end) = ToLocalRange(entry.Start, stop);
        return PlannedWorkItem.Create(
            date,
            name: entry.Description,
            comment: entry.Description,
            duration: stop - entry.Start,
            start: start,
            end: end) with
        {
            TogglEntryId = entry.Id,
            TogglProjectId = entry.ProjectId,
            Source = ItemSource.Toggl,
            PostToToggl = false
        };
    }

    private static (TimeOnly Start, TimeOnly End) ToLocalRange(DateTimeOffset start, DateTimeOffset end) =>
        (TimeOnly.FromDateTime(start.LocalDateTime), TimeOnly.FromDateTime(end.LocalDateTime));
}
