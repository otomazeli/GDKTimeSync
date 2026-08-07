using System.IO;
using System.Text.Json;

namespace GDK.TimeSync.Desktop.Services;

public sealed class UserSettingsService : IUserSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string settingsPath;

    public UserSettingsService()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GDK", "TimeSync", "settings.json"))
    {
    }

    internal UserSettingsService(string settingsPath) => this.settingsPath = settingsPath;

    public UserSettings Load()
    {
        if (!File.Exists(settingsPath)) return new UserSettings();
        try { return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(settingsPath), SerializerOptions) ?? new UserSettings(); }
        catch (JsonException) { return new UserSettings(); }
    }

    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var temporaryPath = settingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, SerializerOptions));
        File.Move(temporaryPath, settingsPath, overwrite: true);
    }
}