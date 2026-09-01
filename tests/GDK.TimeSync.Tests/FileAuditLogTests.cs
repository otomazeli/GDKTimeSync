using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.Services;

namespace GDK.TimeSync.Tests;

public sealed class FileAuditLogTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"GDK.TimeSync.Audit.{Guid.NewGuid():N}");

    [Fact]
    public void Write_AppendsATimestampedEntryToTodaysFile()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 1, 14, 2, 11, 884, TimeSpan.Zero));
        var log = new FileAuditLog(directory, clock);

        log.Write(AuditLevel.Error, "Tempo.CreateWorklog", "POST /worklogs -> 400 BadRequest");

        var text = File.ReadAllText(log.CurrentFilePath);
        Assert.EndsWith("timesync-20260901.log", log.CurrentFilePath, StringComparison.Ordinal);
        Assert.Contains("ERROR Tempo.CreateWorklog", text, StringComparison.Ordinal);
        Assert.Contains("POST /worklogs -> 400 BadRequest", text, StringComparison.Ordinal);
        Assert.Contains("2026-09-01 14:02:11.884", text, StringComparison.Ordinal);
    }

    // Every Write emits a header line plus its indented body, so 200 writes produce 400 lines.
    // What matters is that no entry is torn or interleaved with another's.
    [Fact]
    public void Write_KeepsConcurrentEntriesWholeAndUninterleaved()
    {
        var log = new FileAuditLog(directory);
        var message = new string('x', 400);

        Parallel.For(0, 200, i => log.Write(AuditLevel.Info, "Sync", $"{i} {message}"));

        var lines = File.ReadAllLines(log.CurrentFilePath);
        var headers = lines.Where(line => line.EndsWith(" INFO  Sync", StringComparison.Ordinal)).ToList();
        var bodies = lines.Where(line => line.StartsWith("  ", StringComparison.Ordinal)).ToList();

        Assert.Equal(400, lines.Length);
        Assert.Equal(200, headers.Count);
        Assert.Equal(200, bodies.Count);
        Assert.All(bodies, line => Assert.EndsWith(message, line, StringComparison.Ordinal));
        Assert.Equal(200, bodies.Distinct().Count());
    }

    [Fact]
    public void Write_NeverThrowsWhenTheDirectoryCannotBeWritten()
    {
        var log = new FileAuditLog(Path.Combine(directory, "\0invalid"));

        var exception = Record.Exception(() => log.Write(AuditLevel.Info, "App", "start"));

        Assert.Null(exception);
    }

    [Fact]
    public void DeleteFilesOlderThan_RemovesStaleFilesAndKeepsRecentOnes()
    {
        Directory.CreateDirectory(directory);
        var stale = Path.Combine(directory, "timesync-20260801.log");
        var recent = Path.Combine(directory, "timesync-20260831.log");
        var unrelated = Path.Combine(directory, "setup.log");
        foreach (var path in new[] { stale, recent, unrelated }) File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-30));
        File.SetLastWriteTimeUtc(recent, DateTime.UtcNow.AddDays(-1));
        File.SetLastWriteTimeUtc(unrelated, DateTime.UtcNow.AddDays(-30));

        new FileAuditLog(directory).DeleteFilesOlderThan(14);

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(unrelated));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); } catch { }
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
