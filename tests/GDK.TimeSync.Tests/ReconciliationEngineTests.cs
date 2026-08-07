using GDK.TimeSync.Core;

namespace GDK.TimeSync.Tests;

public sealed class ReconciliationEngineTests
{
    [Fact]
    public void Compare_reports_the_seconds_difference_between_toggl_and_tempo()
    {
        SourceTimeEntry[] source = [new SourceTimeEntry("toggl-1", "CGM | CGMFRAVII-2767 | Knowledge Transfer", DateTimeOffset.UtcNow, 1_800)];
        TempoWorklogSnapshot[] tempo = [new TempoWorklogSnapshot("tempo-1", 1_500)];

        var result = ReconciliationEngine.Compare(source, tempo);

        Assert.Equal(1_800, result.TogglSeconds);
        Assert.Equal(1_500, result.TempoSeconds);
        Assert.Equal(300, result.DifferenceSeconds);
    }
}
