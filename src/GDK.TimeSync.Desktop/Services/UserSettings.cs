namespace GDK.TimeSync.Desktop.Services;

public sealed record UserSettings
{
    public string JiraBaseUrl { get; init; } = string.Empty;

    public bool IsConfigured => Uri.TryCreate(JiraBaseUrl, UriKind.Absolute, out _);
}
