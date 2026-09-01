using System.Globalization;
using System.IO;
using System.Text;

namespace GDK.TimeSync.Desktop.Services;

public sealed class AuditLogReader(string logDirectory)
{
    public string CurrentFilePath =>
        Path.Combine(logDirectory, $"timesync-{DateTime.Now:yyyyMMdd}.log");

    public IReadOnlyList<string> ReadRecentEntries(int maxEntries)
    {
        try
        {
            // FileShare.ReadWrite: the writer must never be blocked by the viewer.
            using var stream = new FileStream(CurrentFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var entries = new List<string>();
            var current = new StringBuilder();
            while (reader.ReadLine() is { } line)
            {
                if (StartsAnEntry(line) && current.Length > 0)
                {
                    entries.Add(current.ToString().TrimEnd());
                    current.Clear();
                }

                current.AppendLine(line);
            }

            if (current.Length > 0) entries.Add(current.ToString().TrimEnd());
            entries.Reverse();
            return entries.Count > maxEntries ? entries[..maxEntries] : entries;
        }
        catch
        {
            return [];
        }
    }

    private static bool StartsAnEntry(string line) =>
        line.Length >= 23 && DateTime.TryParseExact(line[..23], "yyyy-MM-dd HH:mm:ss.fff",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
