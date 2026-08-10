namespace GDK.TimeSync.Toggl;

public interface ITogglClient : IDisposable
{
    Task<IReadOnlyList<TogglTimeEntry>> GetTimeEntriesAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);
}
