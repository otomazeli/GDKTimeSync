using System.Text.Json.Serialization;
namespace GDK.TimeSync.Desktop.Services;

public sealed record UserSettings
{
    public string JiraBaseUrl { get; init; } = string.Empty;
    public string JiraUser { get; init; } = string.Empty;
    public long? TogglWorkspaceId { get; init; }
    public string ReviewReminderTime { get; init; } = "16:00";
    public EndOfDayReminderMode EndOfDayReminderMode { get; init; } = EndOfDayReminderMode.Both;
    public string DefaultTempoWorkCategory { get; init; } = "DEVELOPMENT";
    public bool AiEnabled { get; init; }
    public bool AutoSyncEnabled { get; init; } = true;
    public int SyncIntervalMinutes { get; init; } = 15;
    public string SlackTitle { get; init; } = "Daily update";
    public string SlackTaskHeading { get; init; } = "Completed tasks";
    public IReadOnlyList<string> SlackExtraLines { get; init; } = [];

    [JsonIgnore]
    public bool IsConfigured => Uri.TryCreate(JiraBaseUrl, UriKind.Absolute, out _);
}
