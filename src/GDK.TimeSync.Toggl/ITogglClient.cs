namespace GDK.TimeSync.Toggl;

public interface ITogglClient : IDisposable
{
    Task<IReadOnlyList<TogglTimeEntry>> GetTimeEntriesAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TogglProject>> GetProjectsAsync(long workspaceId, CancellationToken cancellationToken = default);
    Task<TogglTimeEntry> CreateTimeEntryAsync(TogglCreateTimeEntryRequest request, CancellationToken cancellationToken = default);
}
