using GDK.TimeSync.Core;

namespace GDK.TimeSync.Tests;

public sealed class TimeEntryParserTests
{
    [Fact]
    public void Parse_returns_a_trimmed_time_entry()
    {
        var parser = new TimeEntryParser(new IssueKeyValidator(new IssueKeyValidationOptions()));

        var result = parser.Parse(" CGM | CGMFRAVII-2767 | Knowledge Transfer ");

        Assert.Equal(new TimeEntry("CGM", "CGMFRAVII-2767", "Knowledge Transfer"), result);
    }
}
