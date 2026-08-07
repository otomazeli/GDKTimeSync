namespace GDK.TimeSync.Core;

public sealed class TimeEntryParser(IssueKeyValidator issueKeyValidator)
{
    public TimeEntry Parse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var parts = input.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace) || !issueKeyValidator.IsValid(parts[1]))
        {
            throw new FormatException("Expected COMPANY | JIRA ISSUE KEY | WORKLOG DESCRIPTION.");
        }

        return new TimeEntry(parts[0], parts[1], parts[2]);
    }
}
