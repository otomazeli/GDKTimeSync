namespace GDK.TimeSync.Core;

public sealed record DescriptionSuggestionRequest(
    Guid PlannedWorkItemId,
    string TaskName,
    string JiraIssueKey,
    string CurrentDescription);
