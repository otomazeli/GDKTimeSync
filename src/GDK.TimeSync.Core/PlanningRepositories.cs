namespace GDK.TimeSync.Core;

public interface IDailyPlanRepository
{
    Task<DailyPlan?> GetAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task SaveAsync(DailyPlan plan, CancellationToken cancellationToken = default);
}

public interface ITemplateRepository
{
    Task<IReadOnlyList<RecurringTaskTemplate>> ListAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(RecurringTaskTemplate template, CancellationToken cancellationToken = default);
}
