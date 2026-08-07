namespace GDK.TimeSync.Jira;

public sealed class JiraOptions
{
    public string BaseUrl { get; set; } = "https://jira.cgm.ag";

    public string PersonalAccessToken { get; set; } = string.Empty;
}
