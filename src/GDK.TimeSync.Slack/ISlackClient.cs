namespace GDK.TimeSync.Slack;

public interface ISlackClient : IDisposable
{
    Task PostAsync(SlackDailyUpdate update, CancellationToken cancellationToken = default);
}
