using System.Windows;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Desktop.ViewModels;

namespace GDK.TimeSync.Desktop;

public partial class SettingsWindow : Window
{
    private readonly IUserSettingsStore settings;
    private readonly SettingsViewModel viewModel;

    public SettingsWindow(IUserSettingsStore settings, SettingsViewModel viewModel)
    {
        this.settings = settings;
        this.viewModel = viewModel;
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        JiraBaseUrlTextBox.Text = settings.Load().JiraBaseUrl;
        await viewModel.LoadAsync();
        UpdateCredentialControls();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        IsEnabled = false;
        try
        {
            await viewModel.SaveAsync(JiraBaseUrlTextBox.Text.Trim(), TogglTokenPasswordBox.Password, JiraTokenPasswordBox.Password);
            TogglTokenPasswordBox.Password = string.Empty;
            JiraTokenPasswordBox.Password = string.Empty;
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

    private void UpdateCredentialControls()
    {
        SetCredentialControls(viewModel.IsTogglTokenConfigured, TogglConfiguredText, TogglReplaceButton, TogglTokenPasswordBox);
        SetCredentialControls(viewModel.IsJiraPatConfigured, JiraConfiguredText, JiraReplaceButton, JiraTokenPasswordBox);
    }

    private static void SetCredentialControls(bool configured, UIElement configuredText, UIElement replaceButton, UIElement passwordBox)
    {
        configuredText.Visibility = configured ? Visibility.Visible : Visibility.Collapsed;
        replaceButton.Visibility = configured ? Visibility.Visible : Visibility.Collapsed;
        passwordBox.Visibility = configured ? Visibility.Collapsed : Visibility.Visible;
    }
}