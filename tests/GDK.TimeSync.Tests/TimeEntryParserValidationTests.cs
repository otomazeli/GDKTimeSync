using GDK.TimeSync.Core;

namespace GDK.TimeSync.Tests;

public sealed class TimeEntryParserValidationTests
{
    [Theory]
    [InlineData("CGM | CGMFRAVII-2767")]
    [InlineData("CGM | CGMFRAVII-2767 | ")]
    [InlineData("CGM | invalid-key | Knowledge Transfer")]
    public void Parse_rejects_malformed_input(string input)
    {
        var parser = new TimeEntryParser(new IssueKeyValidator(new IssueKeyValidationOptions()));

        Assert.Throws<FormatException>(() => parser.Parse(input));
    }

    [Fact]
    public void Parse_accepts_an_added_project_pattern()
    {
        var options = new IssueKeyValidationOptions();
        options.Patterns.Add("^PROJ-\\d{4}$");
        var parser = new TimeEntryParser(new IssueKeyValidator(options));

        var result = parser.Parse("ACME | PROJ-1234 | Work");

        Assert.Equal("PROJ-1234", result.JiraIssueKey);
    }
}
