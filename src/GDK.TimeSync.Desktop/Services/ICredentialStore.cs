namespace GDK.TimeSync.Desktop.Services;

public interface ICredentialStore
{
    Task SaveAsync(string key, string secret, CancellationToken cancellationToken = default);
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
