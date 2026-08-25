using GDK.TimeSync.Core;
using GDK.TimeSync.Toggl;

namespace GDK.TimeSync.Desktop.Services;

public sealed class TogglSyncService(
    IIntegrationClientFactory clients,
    IUserSettingsStore settings,
    IDeliveryAttemptRepository attempts,
    IssueKeyValidator issueKeyValidator) : ITogglSyncService
{
    public async Task<TogglSyncPullResult> PullAsync(DateOnly date, IReadOnlyList<PlannedWorkItem> localItems, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localItems);

        long workspaceId;
        string defaultTempoCategory;
        try
        {
            var configuration = settings.Load();
            if (configuration.TogglWorkspaceId is not > 0)
                return TogglSyncPullResult.Empty("Toggl is not configured.");
            workspaceId = configuration.TogglWorkspaceId.Value;
            defaultTempoCategory = string.IsNullOrWhiteSpace(configuration.DefaultTempoWorkCategory)
                ? "DEVELOPMENT"
                : configuration.DefaultTempoWorkCategory;
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
                itemsToAdd.Add(BuildImportedItem(date, entry, stop, defaultTempoCategory));
                continue;
            }

            var attempt = await TryGetAttemptAsync(localItem.Id, cancellationToken);
            var (entryStart, entryEnd) = ToLocalRange(entry.Start, stop);
            var (parsedJiraKey, parsedComment) = ParseDescription(entry.Description);

            // Whether the remote entry itself drifted from what was already delivered -- the
            // only thing that matters for the Succeeded/reconciliation decision below. Local
            // field backfills (jiraKeyToFill/tempoCategoryToFill) are not a "the remote changed"
            // signal and must never trigger reconciliation on an already-succeeded delivery.
            var remoteChanged = localItem.Start != entryStart || localItem.End != entryEnd ||
                                 !string.Equals(localItem.Comment, parsedComment, StringComparison.Ordinal);

            if (attempt is { Status: DeliveryAttemptStatus.Succeeded })
            {
                if (remoteChanged && attempt.FailureCode != DeliveryFailureCode.RemoteChangedAfterDelivery)
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

            var jiraKeyToFill = string.IsNullOrWhiteSpace(localItem.JiraIssueKey) && !string.IsNullOrEmpty(parsedJiraKey) ? parsedJiraKey : null;
            var tempoCategoryToFill = string.IsNullOrWhiteSpace(localItem.TempoCategory) ? defaultTempoCategory : null;
            var changed = remoteChanged || jiraKeyToFill is not null || tempoCategoryToFill is not null;

            if (!changed && localItem.TogglEntryId == entry.Id)
                continue;

            itemsToUpdate.Add(localItem with
            {
                Start = entryStart,
                End = entryEnd,
                Comment = parsedComment,
                Duration = stop - entry.Start,
                TogglEntryId = entry.Id,
                JiraIssueKey = jiraKeyToFill ?? localItem.JiraIssueKey,
                TempoCategory = tempoCategoryToFill ?? localItem.TempoCategory
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

    private PlannedWorkItem BuildImportedItem(DateOnly date, TogglTimeEntry entry, DateTimeOffset stop, string defaultTempoCategory)
    {
        var (start, end) = ToLocalRange(entry.Start, stop);
        var (jiraIssueKey, comment) = ParseDescription(entry.Description);
        return PlannedWorkItem.Create(
            date,
            name: comment,
            jiraIssueKey: jiraIssueKey,
            comment: comment,
            duration: stop - entry.Start,
            tempoCategory: defaultTempoCategory,
            start: start,
            end: end) with
        {
            TogglEntryId = entry.Id,
            TogglProjectId = entry.ProjectId,
            Source = ItemSource.Toggl,
            PostToToggl = false
        };
    }

    // Entries typed directly in Toggl commonly lead with the Jira key, in one of several
    // shapes: "CGMFRAVII-8139 Proxy DMP : Impact et endpoints" (plain space),
    // "CGMFRAVII-2763 - AxiSanté Agile Meetings" (dash-separated), or
    // "CGMFRAVII-8424 | DMP — Infrastructure" (pipe-separated). Extract the leading
    // whitespace-delimited token using the same pattern the app validates keys against
    // elsewhere; if it looks like a key, drop it (and an optional following "-"/"|") from
    // the comment so it isn't duplicated once the key has its own field (Slack/Tempo
    // formatting already prepends the key separately).
    private static readonly char[] DescriptionSeparators = ['-', '|'];

    private (string JiraIssueKey, string Comment) ParseDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return ("", description ?? "");

        var trimmed = description.TrimStart();
        var spaceIndex = trimmed.IndexOf(' ');
        var candidate = spaceIndex < 0 ? trimmed : trimmed[..spaceIndex];
        if (!issueKeyValidator.IsValid(candidate)) return ("", description);

        var remainder = spaceIndex < 0 ? "" : trimmed[(spaceIndex + 1)..].TrimStart();
        if (remainder.Length > 0 && DescriptionSeparators.Contains(remainder[0]))
            remainder = remainder[1..].TrimStart();

        return (candidate, remainder);
    }

    private static (TimeOnly Start, TimeOnly End) ToLocalRange(DateTimeOffset start, DateTimeOffset end) =>
        (TimeOnly.FromDateTime(start.LocalDateTime), TimeOnly.FromDateTime(end.LocalDateTime));
}
