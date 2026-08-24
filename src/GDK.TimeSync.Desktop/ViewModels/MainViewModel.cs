using System.ComponentModel;
using System.Runtime.CompilerServices;
using GDK.TimeSync.Desktop.Services;

namespace GDK.TimeSync.Desktop.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IConfigurationStateService configurationState;
    private readonly ITogglSyncService? syncService;
    private readonly TodayViewModel? today;
    private bool isSynchronizing;
    private string statusText = "Not configured. Open Settings to add a Toggl API token, Jira URL, and Jira personal access token.";
    private string? syncStatusText;

    public MainViewModel(IConfigurationStateService configurationState, ITogglSyncService? syncService = null, TodayViewModel? today = null)
    {
        this.configurationState = configurationState;
        this.syncService = syncService;
        this.today = today;
        configurationState.ConfigurationChanged += (_, _) => UpdateConfigurationStatus();
        SyncNowCommand = new RelayCommand(() => _ = SyncNowAsync(), () => configurationState.IsConfigured && !IsSynchronizing);
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
            SyncStatusText = result.Error is not null
                ? "Sync failed: Toggl is not reachable or not configured."
                : FormatSyncSummary(today.ApplyPullResult(result));
        }
        catch
        {
            SyncStatusText = "Sync failed: Toggl is not reachable or not configured.";
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
