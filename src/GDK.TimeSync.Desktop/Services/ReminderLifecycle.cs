namespace GDK.TimeSync.Desktop.Services;

internal static class ReminderLifecycle
{
    public static void BeginStop(
        IEndOfDayReminderService? reminder,
        EventHandler<ReviewDueEventArgs> handler)
    {
        if (reminder is null) return;

        reminder.ReviewDue -= handler;
        _ = StopIgnoringFailureAsync(reminder);
    }

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

    private static async Task StopIgnoringFailureAsync(IEndOfDayReminderService reminder)
    {
        try
        {
            await reminder.StopAsync().ConfigureAwait(false);
        }
        catch (Exception) { }
    }
}
