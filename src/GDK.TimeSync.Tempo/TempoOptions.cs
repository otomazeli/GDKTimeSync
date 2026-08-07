namespace GDK.TimeSync.Tempo;

public sealed class TempoOptions
{
    public string BaseUrl { get; set; } = "https://jira.cgm.ag";

    public string PersonalAccessToken { get; set; } = string.Empty;
}
