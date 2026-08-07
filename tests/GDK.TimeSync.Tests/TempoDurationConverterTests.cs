using GDK.TimeSync.Core;

namespace GDK.TimeSync.Tests;

public sealed class TempoDurationConverterTests
{
    [Fact]
    public void ToSeconds_returns_the_same_whole_second_value() =>
        Assert.Equal(5_400, TempoDurationConverter.ToSeconds(5_400));

    [Fact]
    public void ToSeconds_rejects_a_running_toggl_duration() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TempoDurationConverter.ToSeconds(-1));
}
