namespace GDK.TimeSync.Core;

public sealed record TempoWorklogSnapshot(string WorklogId, long TimeSpentSeconds);
public sealed record ReconciliationResult(long TogglSeconds, long TempoSeconds)
{
    public long DifferenceSeconds => TogglSeconds - TempoSeconds;
}

public static class ReconciliationEngine
{
    public static ReconciliationResult Compare(IEnumerable<SourceTimeEntry> sourceEntries, IEnumerable<TempoWorklogSnapshot> tempoWorklogs) =>
        new(sourceEntries.Where(entry => entry.DurationSeconds >= 0).Sum(entry => entry.DurationSeconds), tempoWorklogs.Sum(worklog => worklog.TimeSpentSeconds));
}
