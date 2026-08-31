namespace GDK.TimeSync.Tempo;

public interface ITempoClient : IDisposable
{
    Task<IReadOnlyList<TempoAttribute>> GetWorkAttributesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TempoWorklog>> GetExistingWorklogsAsync(string originTaskId, CancellationToken cancellationToken = default);
    Task<TempoWorklog> CreateWorklogAsync(TempoWorklogRequest request, CancellationToken cancellationToken = default);
    Task<TempoWorklog?> GetWorklogAsync(long worklogId, CancellationToken cancellationToken = default);
    Task<TempoWorklog> UpdateWorklogAsync(long worklogId, TempoWorklogRequest request, CancellationToken cancellationToken = default);
}
