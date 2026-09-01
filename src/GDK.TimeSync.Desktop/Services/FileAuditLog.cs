using System.Globalization;
using System.IO;
using System.Text;
using GDK.TimeSync.Core;

namespace GDK.TimeSync.Desktop.Services;

public sealed class FileAuditLog(string logDirectory, TimeProvider? timeProvider = null) : IAuditLog
{
    private const string FilePrefix = "timesync-";
    private const string FileSuffix = ".log";

    // ponytail: one global lock around the append. Fine at this volume (tens of lines a minute);
    // move to a Channel<T> with a single writer task if logging ever shows up in a UI stall.
    private readonly Lock appendLock = new();
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public string CurrentFilePath =>
        Path.Combine(logDirectory, $"{FilePrefix}{Now():yyyyMMdd}{FileSuffix}");

    public void Write(AuditLevel level, string category, string message)
    {
        try
        {
            var line = $"{Now():yyyy-MM-dd HH:mm:ss.fff} {Label(level)} {category}{Environment.NewLine}{Indent(message)}";
            lock (appendLock)
            {
                Directory.CreateDirectory(logDirectory);
                File.AppendAllText(CurrentFilePath, line + Environment.NewLine, new UTF8Encoding(false));
            }
        }
        catch
        {
            // A failure to record must never change what the application does.
        }
    }

    public void DeleteFilesOlderThan(int days)
    {
        try
        {
            var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-days);
            foreach (var path in Directory.EnumerateFiles(logDirectory, $"{FilePrefix}*{FileSuffix}"))
                if (File.GetLastWriteTimeUtc(path) < cutoff)
                    File.Delete(path);
        }
        catch
        {
        }
    }

    private DateTime Now() => clock.GetLocalNow().DateTime;

    private static string Label(AuditLevel level) => level switch
    {
        AuditLevel.Error => "ERROR",
        AuditLevel.Warning => "WARN ",
        _ => "INFO "
    };

    // Continuation lines are indented so a reader (and AuditLogReader) can tell an entry's first
    // line, which starts with a timestamp in column 1, from the lines belonging to it.
    private static string Indent(string message) =>
        string.Join(Environment.NewLine, message.ReplaceLineEndings("\n").Split('\n').Select(line => "  " + line));
}
