namespace GDK.TimeSync.Desktop.Services;

public sealed record ReviewReminderActions(bool ShowTrayNotification, bool OpenReviewWindow)
{
    public static ReviewReminderActions From(EndOfDayReminderMode mode) => mode switch
    {
        EndOfDayReminderMode.TrayNotificationOnly => new(true, false),
        EndOfDayReminderMode.OpenReviewOnly => new(false, true),
        _ => new(true, true)
    };
}

internal static class ReviewReminderPresenter
{
    public static async Task PresentAsync(
        EndOfDayReminderMode mode,
        Action showTrayNotification,
        Func<Task> openReviewWindow)
    {
        var actions = ReviewReminderActions.From(mode);
        if (actions.ShowTrayNotification) showTrayNotification();
        if (actions.OpenReviewWindow) await openReviewWindow();
    }
}
