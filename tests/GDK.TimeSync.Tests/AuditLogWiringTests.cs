using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GDK.TimeSync.Tests;

public sealed class AuditLogWiringTests
{
    [Fact]
    public void ConfigureServices_RegistersASingleAuditLog()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IAuditLog>();
        var second = provider.GetRequiredService<IAuditLog>();

        Assert.IsType<FileAuditLog>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void LogDirectory_SitsBesideSettingsUnderGdkTimeSync()
    {
        Assert.EndsWith(Path.Combine("GDK", "TimeSync", "logs"), App.LogDirectory, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SyncNowAsync_RecordsTheOutcomeCounts()
    {
        var log = new RecordingAuditLog();
        var today = new TodayViewModel(date: new DateOnly(2026, 9, 1));
        var main = new MainViewModel(new StubConfigurationState(), new StubSyncService(), today, auditLog: log);

        await main.SyncNowAsync();

        Assert.Contains(log.Entries, entry => entry.Category == "Sync" && entry.Message.Contains("Imported 0", StringComparison.Ordinal));
    }

    private sealed record Entry(AuditLevel Level, string Category, string Message);

    private sealed class RecordingAuditLog : IAuditLog
    {
        public List<Entry> Entries { get; } = [];
        public void Write(AuditLevel level, string category, string message) => Entries.Add(new Entry(level, category, message));
    }

    private sealed class StubConfigurationState : IConfigurationStateService
    {
        public bool IsConfigured => true;
        public bool HasTogglCredential => true;
        public bool HasJiraCredential => true;
        public event EventHandler? ConfigurationChanged { add { } remove { } }
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubSyncService : ITogglSyncService
    {
        public Task<TogglSyncPullResult> PullAsync(DateOnly date, IReadOnlyList<PlannedWorkItem> localItems, CancellationToken cancellationToken = default) =>
            Task.FromResult(TogglSyncPullResult.Empty());
    }
}
