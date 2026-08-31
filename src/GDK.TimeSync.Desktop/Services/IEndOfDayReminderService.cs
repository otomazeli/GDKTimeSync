namespace GDK.TimeSync.Desktop.Services;

public interface IEndOfDayReminderService
{
    event EventHandler<ReviewDueEventArgs>? ReviewDue;

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
