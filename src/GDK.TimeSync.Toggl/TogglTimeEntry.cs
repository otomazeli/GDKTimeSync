namespace GDK.TimeSync.Toggl;

public sealed class TogglTimeEntry
{
    public long Id { get; init; }
    public string Description { get; init; } = string.Empty;
    public DateTimeOffset Start { get; init; }
    public DateTimeOffset? Stop { get; init; }

    [JsonPropertyName("duration")]
    public long DurationSeconds { get; init; }

    [JsonPropertyName("workspace_id")]
    public long WorkspaceId { get; init; }

    [JsonPropertyName("project_id")]
    public long? ProjectId { get; init; }
}
