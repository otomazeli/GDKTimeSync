namespace GDK.TimeSync.Core;

public enum SyncMode { Apply, DryRun }
public enum SyncOutcomeStatus { Created, DryRun, SkippedDuplicate, Invalid }
public sealed record SyncOutcome(SyncOutcomeStatus Status, string? Message = null);

public sealed class SyncEngine(TimeEntryParser parser, IJiraIssueValidator jira, ITempoWorklogWriter tempo, ISyncStateStore state)
{
    public async Task<SyncOutcome> SynchronizeAsync(SourceTimeEntry source, SyncMode mode, CancellationToken cancellationToken = default)
    {
        if (source.DurationSeconds < 0)
            return new(SyncOutcomeStatus.Invalid, "Running Toggl entries are not synchronized.");

        TimeEntry entry;
        try { entry = parser.Parse(source.Description); }
        catch (FormatException exception) { return new(SyncOutcomeStatus.Invalid, exception.Message); }

        if (!await jira.ExistsAsync(entry.JiraIssueKey, cancellationToken))
            return new(SyncOutcomeStatus.Invalid, "Jira issue was not found.");

        if (await state.IsSynchronizedAsync(source.SourceEntryId, cancellationToken))
            return new(SyncOutcomeStatus.SkippedDuplicate);

        if (mode == SyncMode.DryRun)
            return new(SyncOutcomeStatus.DryRun);

        await tempo.CreateAsync(new(entry.JiraIssueKey, source.Started, TempoDurationConverter.ToSeconds(source.DurationSeconds), entry.WorklogDescription), cancellationToken);
        await state.MarkSynchronizedAsync(source.SourceEntryId, cancellationToken);
        return new(SyncOutcomeStatus.Created);
    }
}
