using System.ComponentModel;
using System.Runtime.CompilerServices;
using GDK.TimeSync.Desktop.Services;

namespace GDK.TimeSync.Desktop.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IConfigurationStateService configurationState;
    private bool isSynchronizing;
    private string statusText = "Not configured. Open Settings to add a Toggl API token, Jira URL, and Jira personal access token.";

    public MainViewModel(IConfigurationStateService configurationState)
    {
        this.configurationState = configurationState;
        configurationState.ConfigurationChanged += (_, _) => UpdateConfigurationStatus();
        SyncNowCommand = new RelayCommand(() => { }, () => configurationState.IsConfigured && !IsSynchronizing);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RelayCommand SyncNowCommand { get; }

    public string StatusText
    {
        get => statusText;
        private set => SetField(ref statusText, value);
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
