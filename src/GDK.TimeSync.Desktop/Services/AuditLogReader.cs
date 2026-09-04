using System.Globalization;
using System.IO;
using System.Text;
using GDK.TimeSync.Core;

namespace GDK.TimeSync.Desktop.Services;

public sealed class AuditLogReader(string logDirectory)
{
    public string CurrentFilePath =>
        // Invariant, matching FileAuditLog.FilePathFor: a non-Gregorian default calendar would
        // otherwise send the reader looking for a file the writer never created.
        Path.Combine(logDirectory, $"timesync-{DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");

    public IReadOnlyList<AuditLogEntry> ReadRecentEntries(int maxEntries)
    {
        try
        {
            // FileShare.ReadWrite: the writer must never be blocked by the viewer.
            using var stream = new FileStream(CurrentFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var entries = new List<AuditLogEntry>();
            var current = new StringBuilder();
            var level = AuditLevel.Info;
            while (reader.ReadLine() is { } line)
            {
                if (StartsAnEntry(line) && current.Length > 0)
                {
                    entries.Add(BuildEntry(current, level));
                    current.Clear();
                }

                if (StartsAnEntry(line)) level = LevelOf(line);
                current.AppendLine(line);
            }

            if (current.Length > 0) entries.Add(BuildEntry(current, level));
            entries.Reverse();
            return entries.Count > maxEntries ? entries[..maxEntries] : entries;
        }
        catch
        {
            return [];
        }
    }

    private static AuditLogEntry BuildEntry(StringBuilder text, AuditLevel level) =>
        new(text.ToString().TrimEnd(), level);

    // FileAuditLog writes "<23-char stamp> <LABEL> <category>", so the label sits at a fixed offset.
    // Anything unrecognised reads as Info rather than colouring a line that did not fail.
    internal static AuditLevel LevelOf(string firstLine)
    {
        const int labelStart = 24;
        if (firstLine.Length < labelStart + 5) return AuditLevel.Info;
        return firstLine.Substring(labelStart, 5) switch
        {
            "ERROR" => AuditLevel.Error,
            "WARN " => AuditLevel.Warning,
            _ => AuditLevel.Info
        };
    }

    private static bool StartsAnEntry(string line) =>
        line.Length >= 23 && DateTime.TryParseExact(line[..23], "yyyy-MM-dd HH:mm:ss.fff",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
