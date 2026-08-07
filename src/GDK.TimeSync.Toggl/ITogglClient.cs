namespace GDK.TimeSync.Toggl;

public interface ITogglClient
{
    Task<IReadOnlyList<TogglTimeEntry>> GetTimeEntriesAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);
}
