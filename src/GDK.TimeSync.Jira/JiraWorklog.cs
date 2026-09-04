namespace GDK.TimeSync.Jira;

// Jira's own worklog, as returned by POST /rest/api/2/issue/{key}/worklog. Note what is absent: no
// worker and no author to get wrong -- Jira attributes the worklog to whoever owns the PAT.
public sealed record JiraWorklog(string Id, int TimeSpentSeconds);
