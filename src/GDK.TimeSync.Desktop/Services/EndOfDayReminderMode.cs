namespace GDK.TimeSync.Desktop.Services;

public enum EndOfDayReminderMode
{
    TrayNotificationOnly,
    OpenReviewOnly,
    Both
}

public static class EndOfDayReminderModes
{
    public static EndOfDayReminderMode Normalize(EndOfDayReminderMode mode) =>
        Enum.IsDefined(mode) ? mode : EndOfDayReminderMode.Both;
}
