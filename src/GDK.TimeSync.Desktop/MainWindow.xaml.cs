using System.Windows;
using GDK.TimeSync.Desktop.Services;

namespace GDK.TimeSync.Desktop;

public partial class MainWindow : Window
{
    private readonly UserSettingsService settings;
    private readonly WindowsCredentialStore credentials;

    public MainWindow(UserSettingsService settings, WindowsCredentialStore credentials)
    {
        this.settings = settings;
        this.credentials = credentials;
        InitializeComponent();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        RefreshStatus();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(settings, credentials) { Owner = this };
        window.ShowDialog();
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var configuration = settings.Load();
        var hasToggl = credentials.HasSecret(WindowsCredentialStore.TogglTokenTarget);
        var hasJira = credentials.HasSecret(WindowsCredentialStore.JiraPatTarget);
        StatusText.Text = configuration.IsConfigured && hasToggl && hasJira
            ? "Toggl and Jira credentials are stored securely. Tempo uses the Jira base URL."
            : "Not configured. Open Settings to add a Toggl API token, Jira URL, and Jira personal access token.";
    }
}