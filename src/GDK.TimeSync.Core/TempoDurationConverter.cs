namespace GDK.TimeSync.Core;

public static class TempoDurationConverter
{
    public static int ToSeconds(long togglDurationSeconds)
    {
        if (togglDurationSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(togglDurationSeconds));
        }

        return checked((int)togglDurationSeconds);
    }
}
