using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GDK.TimeSync.Desktop.Services;

namespace GDK.TimeSync.Desktop.ViewModels;

public enum ConnectionStatus { Checking, Connected, Failed }

public sealed class ConnectionStatusItem(string name) : INotifyPropertyChanged
{
    private ConnectionStatus status = ConnectionStatus.Checking;
    private string message = "Checking...";

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Name { get; } = name;
    public ConnectionStatus Status { get => status; private set => SetField(ref status, value); }
    public string Message { get => message; private set => SetField(ref message, value); }

    public void Set(ConnectionStatus nextStatus, string nextMessage)
    {
        Status = nextStatus;
        Message = nextMessage;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class ConnectionStatusViewModel : INotifyPropertyChanged
{
    private readonly IIntegrationDiagnosticsService diagnostics;
    private readonly ISlackClientFactory slack;
    private bool isRefreshing;

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<ConnectionStatusItem> Items { get; } =
    [new("Toggl"), new("Jira"), new("Slack")];
    public ConnectionStatusItem Toggl => Items[0];
    public ConnectionStatusItem Jira => Items[1];
    public ConnectionStatusItem Slack => Items[2];
    public bool IsRefreshing { get => isRefreshing; private set => SetField(ref isRefreshing, value); }
    public RelayCommand RefreshCommand { get; }

    public ConnectionStatusViewModel(IIntegrationDiagnosticsService diagnostics, ISlackClientFactory slack)
    {
        this.diagnostics = diagnostics;
        this.slack = slack;
        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync(), () => !IsRefreshing);
    }

    public ConnectionStatusViewModel()
        : this(new UnavailableDiagnostics(), new UnavailableSlackFactory()) { }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        try
        {
            var results = await diagnostics.RunAsync(cancellationToken);
            Apply(Toggl, results.FirstOrDefault(result => result.Target == IntegrationDiagnosticTarget.Toggl));
            Apply(Jira, results.FirstOrDefault(result => result.Target == IntegrationDiagnosticTarget.Jira));
            var configured = await slack.IsConfiguredAsync(cancellationToken);
            Slack.Set(configured ? ConnectionStatus.Connected : ConnectionStatus.Failed, configured ? "Configured" : "Not configured");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Toggl.Set(ConnectionStatus.Failed, "Unavailable");
            Jira.Set(ConnectionStatus.Failed, "Unavailable");
            Slack.Set(ConnectionStatus.Failed, "Unavailable");
        }
        catch
        {
            Toggl.Set(ConnectionStatus.Failed, "Unavailable");
            Jira.Set(ConnectionStatus.Failed, "Unavailable");
            Slack.Set(ConnectionStatus.Failed, "Unavailable");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private static void Apply(ConnectionStatusItem item, IntegrationDiagnosticResult? result) =>
        item.Set(result?.IsSuccessful == true ? ConnectionStatus.Connected : ConnectionStatus.Failed, result?.IsSuccessful == true ? "Connected" : "Failed");

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        RefreshCommand.NotifyCanExecuteChanged();
    }

    private sealed class UnavailableDiagnostics : IIntegrationDiagnosticsService
    {
        public Task<IReadOnlyList<IntegrationDiagnosticResult>> RunAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<IntegrationDiagnosticResult>>([]);
    }

    private sealed class UnavailableSlackFactory : ISlackClientFactory
    {
        public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<GDK.TimeSync.Slack.ISlackClient> CreateAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
