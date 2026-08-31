namespace GDK.TimeSync.Core;

public sealed record DescriptionSuggestionRequest(
    string TaskName,
    string JiraIssueKey,
    string CurrentDescription);
