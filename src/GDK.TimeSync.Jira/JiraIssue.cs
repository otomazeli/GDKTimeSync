namespace GDK.TimeSync.Jira;

public sealed record JiraIssue(string Key, JiraIssueFields Fields)
{
    public string? Summary => Fields.Summary;
}

public sealed record JiraIssueFields(string? Summary);
