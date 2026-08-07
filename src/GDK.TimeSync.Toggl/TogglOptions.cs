namespace GDK.TimeSync.Toggl;

public sealed class TogglOptions
{
    public string BaseUrl { get; set; } = "https://api.track.toggl.com/api/v9/";

    public string ApiToken { get; set; } = string.Empty;
}
