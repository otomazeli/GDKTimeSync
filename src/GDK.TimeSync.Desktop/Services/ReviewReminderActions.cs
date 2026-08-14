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
