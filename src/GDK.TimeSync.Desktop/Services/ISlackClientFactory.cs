using GDK.TimeSync.Slack;

namespace GDK.TimeSync.Desktop.Services;

public interface ISlackClientFactory
{
    Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default);
    Task<ISlackClient> CreateAsync(CancellationToken cancellationToken = default);
}
