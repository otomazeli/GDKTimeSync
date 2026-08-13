using System.Text.Json;
using GDK.TimeSync.Desktop.Services;

namespace GDK.TimeSync.Tests;

public sealed class UserSettingsServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"GDK.TimeSync.Tests.{Guid.NewGuid():N}");

    [Theory]
    [InlineData("https://hooks.slack.com/services/T000/B000/sentinel-webhook")]
    [InlineData("https://example.test/post?authorization=sentinel-secret")]
    [InlineData("Bearer sentinel-secret")]
    public void Direct_save_rejects_sensitive_slack_presentation_text_without_writing_json(string sensitiveText)
    {
        var path = Path.Combine(directory, "settings.json");
        var service = new UserSettingsService(path);

        var exception = Assert.Throws<ArgumentException>(() => service.Save(new UserSettings { SlackTitle = sensitiveText }));

        Assert.Equal("Slack presentation preferences must not contain sensitive content.", exception.Message);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Load_rewrites_tampered_sensitive_presentation_text_before_it_can_be_serialized()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        const string sentinel = "https://hooks.slack.com/services/T000/B000/sentinel-webhook";
        File.WriteAllText(path, $$"""{"SlackTitle":"{{sentinel}}","SlackTaskHeading":"Completed","SlackExtraLines":["See https://example.test/docs"]}""");
        var service = new UserSettingsService(path);

        var loaded = service.Load();
        var json = JsonSerializer.Serialize(loaded);

        Assert.Equal("Daily update", loaded.SlackTitle);
        Assert.Equal("Completed", loaded.SlackTaskHeading);
        Assert.Equal(["See https://example.test/docs"], loaded.SlackExtraLines);
        Assert.DoesNotContain(sentinel, json, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, File.ReadAllText(path), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
