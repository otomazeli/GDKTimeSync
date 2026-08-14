using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GDK.TimeSync.Desktop.Services;

public sealed class UserSettingsService : IUserSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private static readonly Regex UrlPattern = new(@"https?://[^\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BearerPattern = new(@"\bbearer\s+\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SlackTokenPattern = new(@"\bxox[baprs]-[\w-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly string settingsPath;

    public UserSettingsService()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GDK", "TimeSync", "settings.json"))
    {
    }

    internal UserSettingsService(string settingsPath) => this.settingsPath = settingsPath;

    public UserSettings Load()
    {
        if (!File.Exists(settingsPath)) return new UserSettings();
        try
        {
            var loaded = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(settingsPath), SerializerOptions) ?? new UserSettings();
            var normalized = NormalizeSettings(loaded, out var changed);
            if (changed) Save(normalized);
            return normalized;
        }
        catch (JsonException) { return new UserSettings(); }
    }

    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateSlackPresentation(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var temporaryPath = settingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, SerializerOptions));
        File.Move(temporaryPath, settingsPath, overwrite: true);
    }

    internal static void ValidateSlackPresentation(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (IsSensitivePresentationText(settings.SlackTitle) || IsSensitivePresentationText(settings.SlackTaskHeading) || (settings.SlackExtraLines ?? []).Any(IsSensitivePresentationText))
            throw new ArgumentException("Slack presentation preferences must not contain sensitive content.");
    }

    private static UserSettings SanitizeSlackPresentation(UserSettings settings, out bool changed)
    {
        var title = IsSensitivePresentationText(settings.SlackTitle) ? "Daily update" : settings.SlackTitle;
        var heading = IsSensitivePresentationText(settings.SlackTaskHeading) ? "Completed tasks" : settings.SlackTaskHeading;
        var existingLines = settings.SlackExtraLines ?? [];
        var extraLines = existingLines.Where(line => !IsSensitivePresentationText(line)).ToArray();
        changed = title != settings.SlackTitle || heading != settings.SlackTaskHeading || extraLines.Length != existingLines.Count || !extraLines.SequenceEqual(existingLines);
        return changed ? settings with { SlackTitle = title, SlackTaskHeading = heading, SlackExtraLines = extraLines } : settings;
    }

    private static UserSettings NormalizeSettings(UserSettings settings, out bool changed)
    {
        var sanitized = SanitizeSlackPresentation(settings, out changed);
        var reminderMode = EndOfDayReminderModes.Normalize(sanitized.EndOfDayReminderMode);
        if (reminderMode == sanitized.EndOfDayReminderMode) return sanitized;
        changed = true;
        return sanitized with { EndOfDayReminderMode = reminderMode };
    }

    private static bool IsSensitivePresentationText(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        if (BearerPattern.IsMatch(value) || SlackTokenPattern.IsMatch(value)) return true;
        foreach (Match match in UrlPattern.Matches(value))
        {
            var candidate = match.Value.TrimEnd('.', ',', ';', ')', ']', '}');
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) continue;
            var path = DecodePath(uri.AbsolutePath);
            if (IsSlackHost(uri.Host) && (path.Contains("/services/", StringComparison.OrdinalIgnoreCase) || (path.Contains("/services", StringComparison.OrdinalIgnoreCase) && path.Contains('%')))) return true;
            if (!string.IsNullOrEmpty(uri.UserInfo) || HasSensitiveQuery(uri.Query)) return true;
        }
        return false;
    }

    private static bool IsSlackHost(string host) => host.Equals("slack.com", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".slack.com", StringComparison.OrdinalIgnoreCase);

    private static string DecodePath(string path)
    {
        for (var count = 0; count < Math.Min(path.Length, 64); count++)
        {
            var decoded = Uri.UnescapeDataString(path);
            if (decoded == path) break;
            path = decoded;
        }
        return path;
    }

    private static bool HasSensitiveQuery(string query) => query.Split('&', StringSplitOptions.RemoveEmptyEntries).Any(pair =>
    {
        var separator = pair.IndexOf('=');
        var name = Uri.UnescapeDataString((separator < 0 ? pair : pair[..separator]).TrimStart('?'));
        return name.Equals("authorization", StringComparison.OrdinalIgnoreCase)
            || name.Equals("token", StringComparison.OrdinalIgnoreCase)
            || name.Equals("secret", StringComparison.OrdinalIgnoreCase)
            || name.Equals("signature", StringComparison.OrdinalIgnoreCase)
            || name.Equals("sig", StringComparison.OrdinalIgnoreCase)
            || name.Equals("access_token", StringComparison.OrdinalIgnoreCase)
            || name.Equals("api_key", StringComparison.OrdinalIgnoreCase)
            || name.Equals("apikey", StringComparison.OrdinalIgnoreCase);
    });
}
