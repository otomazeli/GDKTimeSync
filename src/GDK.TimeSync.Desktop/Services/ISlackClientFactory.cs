using GDK.TimeSync.Slack;

namespace GDK.TimeSync.Desktop.Services;

public interface ISlackClientFactory
{
    Task<ISlackClient> CreateAsync(CancellationToken cancellationToken = default);
}
