using System.Text.Json.Serialization;
namespace GDK.TimeSync.Desktop.Services;

public sealed record UserSettings
{
    public string JiraBaseUrl { get; init; } = string.Empty;
    public long? TogglWorkspaceId { get; init; }
    public bool AutoSyncEnabled { get; init; } = true;
    public int SyncIntervalMinutes { get; init; } = 15;

    [JsonIgnore]
    public bool IsConfigured => Uri.TryCreate(JiraBaseUrl, UriKind.Absolute, out _);
}