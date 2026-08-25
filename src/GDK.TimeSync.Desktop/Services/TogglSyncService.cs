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
            var (parsedJiraKey, parsedComment) = ParseDescription(entry.Description);
            var jiraKeyToFill = string.IsNullOrWhiteSpace(localItem.JiraIssueKey) && !string.IsNullOrEmpty(parsedJiraKey) ? parsedJiraKey : null;
            var changed = localItem.Start != entryStart || localItem.End != entryEnd ||
                          !string.Equals(localItem.Comment, parsedComment, StringComparison.Ordinal) ||
                          jiraKeyToFill is not null;

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
                Comment = parsedComment,
                Duration = stop - entry.Start,
                TogglEntryId = entry.Id,
                JiraIssueKey = jiraKeyToFill ?? localItem.JiraIssueKey
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

    private PlannedWorkItem BuildImportedItem(DateOnly date, TogglTimeEntry entry, DateTimeOffset stop)
    {
        var (start, end) = ToLocalRange(entry.Start, stop);
        var (jiraIssueKey, comment) = ParseDescription(entry.Description);
        return PlannedWorkItem.Create(
            date,
            name: comment,
            jiraIssueKey: jiraIssueKey,
            comment: comment,
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

    // Entries typed directly in Toggl commonly lead with the Jira key, e.g.
    // "CGMFRAVII-2763 - AxiSanté Agile Meetings and Activities 2026 - Daily Squad".
    // Extract that leading key using the same pattern the app validates keys against
    // elsewhere, and drop it from the comment so it isn't duplicated once the key has
    // its own field (Slack/Tempo formatting already prepends the key separately).
    private (string JiraIssueKey, string Comment) ParseDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return ("", description ?? "");

        var separatorIndex = description.IndexOf(" - ", StringComparison.Ordinal);
        if (separatorIndex <= 0) return ("", description);

        var candidate = description[..separatorIndex];
        if (!issueKeyValidator.IsValid(candidate)) return ("", description);

        return (candidate, description[(separatorIndex + 3)..].Trim());
    }

    private static (TimeOnly Start, TimeOnly End) ToLocalRange(DateTimeOffset start, DateTimeOffset end) =>
        (TimeOnly.FromDateTime(start.LocalDateTime), TimeOnly.FromDateTime(end.LocalDateTime));
}
