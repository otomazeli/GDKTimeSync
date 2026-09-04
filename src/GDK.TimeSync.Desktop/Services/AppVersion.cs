using System.Globalization;
using System.IO;
using System.Reflection;

namespace GDK.TimeSync.Desktop.Services;

// Which build is this? The window and the log both said "1.0.0.0" for every build ever made, so a
// stale executable on a machine nobody can debug looked exactly like a current one -- and a day of
// testing went into a build that did not contain the fix being tested.
public static class AppVersion
{
    public static string Display { get; } = Format(ReadInformationalVersion(), ReadBuiltAt());

    // The build stamp comes from the executable's own timestamp rather than a compiled-in constant:
    // copying a folder preserves it, so it stays true about the file actually running.
    internal static string Format(string? informationalVersion, DateTime? builtAt)
    {
        var version = string.IsNullOrWhiteSpace(informationalVersion) ? "unknown" : informationalVersion.Trim();
        return builtAt is { } built
            ? $"v{version} · built {built.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}"
            : $"v{version}";
    }

    private static string? ReadInformationalVersion()
    {
        var assembly = typeof(AppVersion).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString();
    }

    private static DateTime? ReadBuiltAt()
    {
        try
        {
            return Environment.ProcessPath is { } path && File.Exists(path)
                ? File.GetLastWriteTime(path)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A version without a timestamp is still worth showing.
            return null;
        }
    }
}
