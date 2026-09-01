using System.ComponentModel;
using System.Runtime.CompilerServices;
using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.Services;

namespace GDK.TimeSync.Desktop.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IConfigurationStateService configurationState;
    private readonly ITogglSyncService? syncService;
    private readonly TodayViewModel? today;
    private readonly IAuditLog? auditLog;
    private bool isSynchronizing;
    private string statusText = "Not configured. Open Settings to add a Toggl API token, Jira URL, and Jira personal access token.";
    private string? syncStatusText;

    public MainViewModel(IConfigurationStateService configurationState, ITogglSyncService? syncService = null, TodayViewModel? today = null, IAuditLog? auditLog = null)
    {
        this.configurationState = configurationState;
        this.syncService = syncService;
        this.today = today;
        this.auditLog = auditLog;
        configurationState.ConfigurationChanged += (_, _) => UpdateConfigurationStatus();
        SyncNowCommand = new RelayCommand(() => _ = SyncNowAsync(), () => configurationState.IsConfigured && !IsSynchronizing);
        // Picking a date is a user-initiated request to see that day, so it pulls straight away
        // rather than waiting out the background interval. This deliberately follows the selected
        // date; only the automatic background sync is pinned to the real current date (TS-033).
        if (today is not null)
            today.DateSelected += (_, _) => _ = SyncNowAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RelayCommand SyncNowCommand { get; }

    public string StatusText
    {
        get => statusText;
        private set => SetField(ref statusText, value);
    }

    public string? SyncStatusText
    {
        get => syncStatusText;
        private set => SetField(ref syncStatusText, value);
    }

    public bool IsSynchronizing
    {
        get => isSynchronizing;
        set
        {
            if (isSynchronizing == value) return;
            isSynchronizing = value;
            OnPropertyChanged();
            SyncNowCommand.NotifyCanExecuteChanged();
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default) => await configurationState.RefreshAsync(cancellationToken);

    public async Task SyncNowAsync(CancellationToken cancellationToken = default)
    {
        if (syncService is null || today is null || IsSynchronizing) return;
        IsSynchronizing = true;
        try
        {
            var result = await syncService.PullAsync(today.Date, today.GetSnapshot().Items, cancellationToken);
            if (result.Error is not null)
            {
                SyncStatusText = "Sync failed: Toggl is not reachable or not configured.";
                auditLog?.Write(AuditLevel.Error, "Sync", $"{today.Date}: sync failed");
            }
            else
            {
                SyncStatusText = FormatSyncSummary(today.ApplyPullResult(result));
                auditLog?.Write(AuditLevel.Info, "Sync", $"{today.Date}: {SyncStatusText}");
            }
        }
        catch
        {
            SyncStatusText = "Sync failed: Toggl is not reachable or not configured.";
            auditLog?.Write(AuditLevel.Error, "Sync", $"{today.Date}: sync failed");
        }
        finally
        {
            IsSynchronizing = false;
        }
    }

    private static string FormatSyncSummary(TodaySyncMergeResult result) =>
        $"Imported {result.Imported}, updated {result.Updated}, {result.ReconciliationFlagged} needs review.";

    private void UpdateConfigurationStatus()
    {
        StatusText = configurationState.IsConfigured
            ? "Toggl and Jira credentials are stored securely. Tempo uses the Jira base URL."
            : "Not configured. Open Settings to add a Toggl API token, Jira URL, and Jira personal access token.";
        SyncNowCommand.NotifyCanExecuteChanged();
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
