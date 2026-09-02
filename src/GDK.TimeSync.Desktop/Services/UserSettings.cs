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
    // this. Stored as the project *name*: it is what the user picks in the dropdown and it survives a
    // project being recreated in Toggl. Today resolves it to a TogglProjectId against the loaded
    // project list when it applies it, because delivery to Toggl needs the id, not the name.
    public string DefaultTogglProject { get; init; } = "CompuGroup Delphi developments (BR) (FIX)";
    public bool AiEnabled { get; init; }
    public bool AutoSyncEnabled { get; init; } = true;
    public int SyncIntervalMinutes { get; init; } = 5;
    public string SlackTitle { get; init; } = "Daily update";
    public string SlackTaskHeading { get; init; } = "Completed tasks";
    public IReadOnlyList<string> SlackExtraLines { get; init; } = [];

    [JsonIgnore]
    public bool IsConfigured => Uri.TryCreate(JiraBaseUrl, UriKind.Absolute, out _);
}
