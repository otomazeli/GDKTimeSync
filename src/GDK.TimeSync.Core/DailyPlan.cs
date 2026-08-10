namespace GDK.TimeSync.Core;

public sealed record DailyPlan(DateOnly Date, IReadOnlyList<PlannedWorkItem> Items)
{
    public static DailyPlan Create(DateOnly date, IReadOnlyList<PlannedWorkItem> items) => new(date, items);
}
