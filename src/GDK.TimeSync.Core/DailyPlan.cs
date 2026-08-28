namespace GDK.TimeSync.Core;

public sealed record DailyPlan(DateOnly Date, IReadOnlyList<PlannedWorkItem> Items)
{
    // The version last read from storage (0 for a plan that has never been saved). SaveAsync
    // uses it for optimistic concurrency: it only succeeds if this still matches the stored row.
    public int Version { get; init; }

    public static DailyPlan Create(DateOnly date, IReadOnlyList<PlannedWorkItem> items) => new(date, items);
}

// Thrown by IDailyPlanRepository.SaveAsync when the stored plan's version no longer matches what
// the caller last read -- another writer (e.g. background Toggl sync) changed it in the meantime.
// The caller should re-read the current plan, reconcile, and retry.
public sealed class PlanConcurrencyException(DateOnly date) : Exception($"The plan for {date:yyyy-MM-dd} was changed by another writer.")
{
    public DateOnly Date { get; } = date;
}
