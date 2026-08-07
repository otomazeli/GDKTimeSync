namespace GDK.TimeSync.Core;

public sealed class InMemorySyncStateStore : ISyncStateStore
{
    private readonly HashSet<string> synchronized = [];

    public Task<bool> IsSynchronizedAsync(string sourceEntryId, CancellationToken cancellationToken = default) =>
        Task.FromResult(synchronized.Contains(sourceEntryId));

    public Task MarkSynchronizedAsync(string sourceEntryId, CancellationToken cancellationToken = default)
    {
        synchronized.Add(sourceEntryId);
        return Task.CompletedTask;
    }
}
