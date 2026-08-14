namespace GDK.TimeSync.Desktop.Services;

public sealed class ReviewDueEventArgs(EndOfDayReminderMode mode) : EventArgs
{
    public EndOfDayReminderMode Mode { get; } = mode;
}
