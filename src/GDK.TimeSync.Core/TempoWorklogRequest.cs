namespace GDK.TimeSync.Core;

public sealed record TempoWorklogRequest(string JiraIssueKey, DateTimeOffset Started, int TimeSpentSeconds, string Comment);
