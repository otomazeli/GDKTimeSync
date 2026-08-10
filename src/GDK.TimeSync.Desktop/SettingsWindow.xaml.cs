using System.Windows;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Desktop.ViewModels;

namespace GDK.TimeSync.Desktop;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await viewModel.LoadAsync();
        JiraBaseUrlTextBox.Text = viewModel.JiraBaseUrl;
        TogglWorkspaceIdTextBox.Text = viewModel.TogglWorkspaceId?.ToString() ?? string.Empty;
        ReviewReminderTimeTextBox.Text = viewModel.ReviewReminderTime;
        DefaultTempoWorkCategoryTextBox.Text = viewModel.DefaultTempoWorkCategory;
        AiEnabledCheckBox.IsChecked = viewModel.AiEnabled;
        UpdateCredentialControls();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        IsEnabled = false;
        try
        {
            if (!long.TryParse(TogglWorkspaceIdTextBox.Text.Trim(), out var workspaceId) && !string.IsNullOrWhiteSpace(TogglWorkspaceIdTextBox.Text))
                throw new ArgumentException("Enter a numeric Toggl workspace ID.");

            viewModel.TogglWorkspaceId = string.IsNullOrWhiteSpace(TogglWorkspaceIdTextBox.Text) ? null : workspaceId;
            viewModel.ReviewReminderTime = ReviewReminderTimeTextBox.Text.Trim();
            viewModel.DefaultTempoWorkCategory = DefaultTempoWorkCategoryTextBox.Text.Trim();
            viewModel.AiEnabled = AiEnabledCheckBox.IsChecked == true;
            await viewModel.SaveAsync(JiraBaseUrlTextBox.Text.Trim(), TogglTokenPasswordBox.Password, JiraTokenPasswordBox.Password, SlackWebhookPasswordBox.Password);
            TogglTokenPasswordBox.Password = string.Empty;
            JiraTokenPasswordBox.Password = string.Empty;
            SlackWebhookPasswordBox.Password = string.Empty;
            DialogResult = true;
        }
        catch (SettingsSaveException exception)
        {
            System.Windows.MessageBox.Show(this, exception.Message, "GDK TimeSync", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (ArgumentException exception)
        {
            System.Windows.MessageBox.Show(this, exception.Message, "GDK TimeSync", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch
        {
            System.Windows.MessageBox.Show(this, "Unable to save settings.", "GDK TimeSync", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void OnReplaceTogglClick(object sender, RoutedEventArgs e)
    {
        TogglConfiguredText.Visibility = Visibility.Collapsed;
        TogglReplaceButton.Visibility = Visibility.Collapsed;
        TogglTokenPasswordBox.Visibility = Visibility.Visible;
        TogglTokenPasswordBox.Focus();
    }

    private void OnReplaceJiraClick(object sender, RoutedEventArgs e)
    {
        JiraConfiguredText.Visibility = Visibility.Collapsed;
        JiraReplaceButton.Visibility = Visibility.Collapsed;
        JiraTokenPasswordBox.Visibility = Visibility.Visible;
        JiraTokenPasswordBox.Focus();
    }

    private void OnReplaceSlackClick(object sender, RoutedEventArgs e)
    {
        SlackConfiguredText.Visibility = Visibility.Collapsed;
        SlackReplaceButton.Visibility = Visibility.Collapsed;
        SlackWebhookPasswordBox.Visibility = Visibility.Visible;
        SlackWebhookPasswordBox.Focus();
    }

    private void UpdateCredentialControls()
    {
        SetCredentialControls(viewModel.IsTogglTokenConfigured, TogglConfiguredText, TogglReplaceButton, TogglTokenPasswordBox);
        SetCredentialControls(viewModel.IsJiraPatConfigured, JiraConfiguredText, JiraReplaceButton, JiraTokenPasswordBox);
        SetCredentialControls(viewModel.IsSlackWebhookConfigured, SlackConfiguredText, SlackReplaceButton, SlackWebhookPasswordBox);
    }

    private static void SetCredentialControls(bool configured, UIElement configuredText, UIElement replaceButton, UIElement passwordBox)
    {
        configuredText.Visibility = configured ? Visibility.Visible : Visibility.Collapsed;
        replaceButton.Visibility = configured ? Visibility.Visible : Visibility.Collapsed;
        passwordBox.Visibility = configured ? Visibility.Collapsed : Visibility.Visible;
    }
}
