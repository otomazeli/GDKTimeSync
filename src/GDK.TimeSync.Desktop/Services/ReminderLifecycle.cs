namespace GDK.TimeSync.Desktop.Services;

internal static class ReminderLifecycle
{
    public static async Task StopThenAsync(
        IEndOfDayReminderService? reminder,
        EventHandler<ReviewDueEventArgs> handler,
        Func<Task> remainingExitAction)
    {
        try
        {
            if (reminder is null) return;

            reminder.ReviewDue -= handler;
            await reminder.StopAsync();
        }
        catch (Exception) { }
        finally
        {
            await remainingExitAction();
        }
    }
}
