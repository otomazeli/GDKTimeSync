using GDK.TimeSync.Desktop.Services;

namespace GDK.TimeSync.Tests;

// "Started version 1.0.0.0" was true of every build ever made, so a stale executable on a machine
// nobody can debug looked exactly like a current one -- and a day of testing went into a build that
// did not contain the fix being tested.
public sealed class AppVersionTests
{
    [Fact]
    public void ShowsTheCommitAndTheBuildStampSoOneBuildCanBeToldFromAnother()
    {
        var display = AppVersion.Format("1.1.0+9412142", new DateTime(2026, 9, 4, 11, 5, 0));

        Assert.Equal("v1.1.0+9412142 · built 2026-09-04 11:05", display);
    }

    [Fact]
    public void StillShowsTheVersionWhenTheBuildStampIsUnavailable()
    {
        Assert.Equal("v1.1.0+9412142", AppVersion.Format("1.1.0+9412142", null));
    }

    // Better an honest "unknown" than a blank corner that reads as "no version at all".
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SaysUnknownRatherThanNothingWhenTheAssemblyCarriesNoVersion(string? informationalVersion)
    {
        Assert.Equal("vunknown", AppVersion.Format(informationalVersion, null));
    }

    // The real assembly, not a hand-made string: this is what the window and the log actually show.
    [Fact]
    public void TheRunningAssemblyReportsARealVersion()
    {
        Assert.StartsWith("v", AppVersion.Display, StringComparison.Ordinal);
        Assert.DoesNotContain("vunknown", AppVersion.Display, StringComparison.Ordinal);
    }
}
