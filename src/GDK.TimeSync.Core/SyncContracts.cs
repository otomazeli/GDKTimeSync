namespace GDK.TimeSync.Core;

public interface IJiraIssueValidator
{
    Task<bool> ExistsAsync(string issueKey, CancellationToken cancellationToken = default);
}

public interface ITempoWorklogWriter
{
    Task CreateAsync(TempoWorklogRequest request, CancellationToken cancellationToken = default);
}

public interface ISyncStateStore
{
    Task<bool> IsSynchronizedAsync(string sourceEntryId, CancellationToken cancellationToken = default);
    Task MarkSynchronizedAsync(string sourceEntryId, CancellationToken cancellationToken = default);
}
