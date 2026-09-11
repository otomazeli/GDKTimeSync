using GDK.TimeSync.Desktop;

namespace GDK.TimeSync.Tests;

public sealed class SingleInstanceTests
{
    [Fact]
    public void The_first_caller_acquires_and_a_second_one_does_not()
    {
        var name = $@"Local\GDK.TimeSync.Tests.{Guid.NewGuid():N}";

        Assert.True(SingleInstance.TryAcquire(name));
        Assert.False(SingleInstance.TryAcquire(name));
    }
}
