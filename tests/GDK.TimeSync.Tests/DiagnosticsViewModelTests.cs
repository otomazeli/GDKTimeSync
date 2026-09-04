using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Desktop.ViewModels;

namespace GDK.TimeSync.Tests;

public sealed class DiagnosticsViewModelTests : IDisposable
{

    // The Diagnostics list was plain text, so a failure had to be found by reading. Each entry now
    // carries its level, which is what lets the view colour the ones that went wrong.
    [Fact]
    public async Task RefreshAsync_MarksTheLevelOfEachEntrySoFailuresCanBeColoured()
    {
        WriteLog("""
            2026-09-04 10:36:43.979 INFO  App
              Started v1.1.0
            2026-09-04 10:37:26.771 ERROR Delivery
              ac4a8171 -> Failed TogglFailed: No Toggl workspace is configured.
            2026-09-04 10:37:26.781 WARN  Review
              Post finished: 0 succeeded, 1 failed
            """);
        var viewModel = new DiagnosticsViewModel(new AuditLogReader(directory));

        await viewModel.RefreshAsync();

        // Newest first, as the reader returns them.
        Assert.Equal(AuditLevel.Warning, viewModel.Entries[0].Level);
        Assert.Equal(AuditLevel.Error, viewModel.Entries[1].Level);
        Assert.Equal(AuditLevel.Info, viewModel.Entries[2].Level);
    }

    // A continuation line can say anything, including the word ERROR inside a logged stack trace.
    // Only the entry's own first line decides its level.
    [Fact]
    public async Task RefreshAsync_TakesTheLevelFromTheEntryNotItsContinuationLines()
    {
        WriteLog("""
            2026-09-04 10:36:43.979 INFO  Sync
              9/4/2026: Imported 0, updated 0, 0 needs review.
              ERROR was mentioned here but this entry is not one
            """);
        var viewModel = new DiagnosticsViewModel(new AuditLogReader(directory));

        await viewModel.RefreshAsync();

        Assert.Equal(AuditLevel.Info, Assert.Single(viewModel.Entries).Level);
    }

    private readonly string directory = Path.Combine(Path.GetTempPath(), $"GDK.TimeSync.Diag.{Guid.NewGuid():N}");

    [Fact]
    public async Task RefreshAsync_ShowsCompleteEntriesNewestFirst()
    {
        WriteLog($"""
            2026-09-01 09:00:00.000 INFO  App
              Started
            2026-09-01 14:02:11.884 ERROR Tempo.CreateWorklog
              POST /rest/tempo-timesheets/4/worklogs -> 400 BadRequest (412 ms)
              response: Worker could not be found
            """);
        var viewModel = new DiagnosticsViewModel(new AuditLogReader(directory));

        await viewModel.RefreshAsync();

        Assert.Equal(2, viewModel.Entries.Count);
        Assert.Contains("Tempo.CreateWorklog", viewModel.Entries[0].Text, StringComparison.Ordinal);
        Assert.Contains("Worker could not be found", viewModel.Entries[0].Text, StringComparison.Ordinal);
        Assert.Contains("Started", viewModel.Entries[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshAsync_CapsTheNumberOfEntriesItShows()
    {
        WriteLog(string.Join(Environment.NewLine,
            Enumerable.Range(0, 600).Select(i => $"2026-09-01 09:00:00.000 INFO  App{Environment.NewLine}  entry {i}")));
        var viewModel = new DiagnosticsViewModel(new AuditLogReader(directory));

        await viewModel.RefreshAsync();

        Assert.Equal(500, viewModel.Entries.Count);
        Assert.Contains("entry 599", viewModel.Entries[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CopyAllCommand_PutsEveryShownEntryOnTheClipboard()
    {
        WriteLog($"2026-09-01 09:00:00.000 INFO  App{Environment.NewLine}  Started");
        var clipboard = new RecordingClipboard();
        var viewModel = new DiagnosticsViewModel(new AuditLogReader(directory), clipboard);
        await viewModel.RefreshAsync();

        viewModel.CopyAllCommand.Execute(null);

        Assert.Contains("Started", clipboard.LastText!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshAsync_ReportsAnEmptyLogWithoutFailing()
    {
        var viewModel = new DiagnosticsViewModel(new AuditLogReader(directory));

        await viewModel.RefreshAsync();

        Assert.Empty(viewModel.Entries);
        Assert.Equal("No entries recorded today.", viewModel.StatusText);
    }

    private void WriteLog(string content)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"timesync-{DateTime.Now:yyyyMMdd}.log"), content);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); } catch { }
    }

    private sealed class RecordingClipboard : IClipboardService
    {
        public string? LastText { get; private set; }
        public void SetText(string text) => LastText = text;
    }
}
