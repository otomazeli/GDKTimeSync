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

    // Jira cannot tell us which Toggl project a piece of work belongs to, so a new row falls back to
    // this. Both halves are stored: the id is what delivery posts and needs no matching, the name
    // keeps settings.json readable and is the fallback for settings written before the id existed.
    // No default value -- the Settings picker is where a real one gets chosen.
    public string DefaultTogglProject { get; init; } = "";
    public long? DefaultTogglProjectId { get; init; }
    public bool AiEnabled { get; init; }
    public bool AutoSyncEnabled { get; init; } = true;
    public int SyncIntervalMinutes { get; init; } = 5;
    public string SlackTitle { get; init; } = "Daily update";
    public string SlackTaskHeading { get; init; } = "Completed tasks";
    public IReadOnlyList<string> SlackExtraLines { get; init; } = [];

    [JsonIgnore]
    public bool IsConfigured => Uri.TryCreate(JiraBaseUrl, UriKind.Absolute, out _);
}
