using System.Windows;
using GDK.TimeSync.Desktop.Services;

namespace GDK.TimeSync.Desktop;

public partial class SettingsWindow : Window
{
    private readonly UserSettingsService settings;
    private readonly WindowsCredentialStore credentials;

    public SettingsWindow(UserSettingsService settings, WindowsCredentialStore credentials)
    {
        this.settings = settings;
        this.credentials = credentials;
        InitializeComponent();
        JiraBaseUrlTextBox.Text = settings.Load().JiraBaseUrl;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var jiraBaseUrl = JiraBaseUrlTextBox.Text.Trim();
        if (!Uri.TryCreate(jiraBaseUrl, UriKind.Absolute, out _))
        {
            System.Windows.MessageBox.Show(this, "Enter an absolute Jira base URL.", "GDK TimeSync", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        settings.Save(new UserSettings { JiraBaseUrl = jiraBaseUrl });
        SaveIfSupplied(WindowsCredentialStore.TogglTokenTarget, TogglTokenPasswordBox.Password);
        SaveIfSupplied(WindowsCredentialStore.JiraPatTarget, JiraTokenPasswordBox.Password);
        DialogResult = true;
    }

    private void SaveIfSupplied(string target, string secret)
    {
        if (!string.IsNullOrWhiteSpace(secret))
            credentials.SaveSecret(target, secret);
    }
}
