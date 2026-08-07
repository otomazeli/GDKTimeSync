using GDK.TimeSync.Core;

namespace GDK.TimeSync.Tests;

public sealed class SyncEngineTests
{
    [Fact]
    public async Task SynchronizeAsync_creates_a_valid_unsynced_entry()
    {
        var tempo = new RecordingTempoWriter();
        var engine = CreateEngine(tempo, new InMemorySyncStateStore());
        var entry = new SourceTimeEntry("toggl-1", "CGM | CGMFRAVII-2767 | Knowledge Transfer", new DateTimeOffset(2026, 8, 7, 8, 15, 0, TimeSpan.Zero), 1_800);

        var result = await engine.SynchronizeAsync(entry, SyncMode.Apply);

        Assert.Equal(SyncOutcomeStatus.Created, result.Status);
        Assert.Equal("CGMFRAVII-2767", Assert.Single(tempo.Created).JiraIssueKey);
        Assert.Equal(1_800, tempo.Created[0].TimeSpentSeconds);
    }

    [Fact]
    public async Task SynchronizeAsync_does_not_write_during_a_dry_run()
    {
        var tempo = new RecordingTempoWriter();
        var engine = CreateEngine(tempo, new InMemorySyncStateStore());
        var entry = new SourceTimeEntry("toggl-1", "CGM | CGMFRAVII-2767 | Knowledge Transfer", new DateTimeOffset(2026, 8, 7, 8, 15, 0, TimeSpan.Zero), 1_800);

        var result = await engine.SynchronizeAsync(entry, SyncMode.DryRun);

        Assert.Equal(SyncOutcomeStatus.DryRun, result.Status);
        Assert.Empty(tempo.Created);
    }

    [Fact]
    public async Task SynchronizeAsync_skips_an_already_processed_source_entry()
    {
        var store = new InMemorySyncStateStore();
        await store.MarkSynchronizedAsync("toggl-1");
        var tempo = new RecordingTempoWriter();
        var engine = CreateEngine(tempo, store);
        var entry = new SourceTimeEntry("toggl-1", "CGM | CGMFRAVII-2767 | Knowledge Transfer", new DateTimeOffset(2026, 8, 7, 8, 15, 0, TimeSpan.Zero), 1_800);

        var result = await engine.SynchronizeAsync(entry, SyncMode.Apply);

        Assert.Equal(SyncOutcomeStatus.SkippedDuplicate, result.Status);
        Assert.Empty(tempo.Created);
    }

    private static SyncEngine CreateEngine(RecordingTempoWriter tempo, ISyncStateStore store) =>
        new(new TimeEntryParser(new IssueKeyValidator(new IssueKeyValidationOptions())), new ValidJiraIssueValidator(), tempo, store);

    private sealed class ValidJiraIssueValidator : IJiraIssueValidator
    {
        public Task<bool> ExistsAsync(string issueKey, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class RecordingTempoWriter : ITempoWorklogWriter
    {
        public List<TempoWorklogRequest> Created { get; } = [];

        public Task CreateAsync(TempoWorklogRequest request, CancellationToken cancellationToken = default)
        {
            Created.Add(request);
            return Task.CompletedTask;
        }
    }
}
