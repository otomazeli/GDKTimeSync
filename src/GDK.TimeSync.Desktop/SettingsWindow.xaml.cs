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
        await viewModel.LoadAsync();
        PopulateControls();
        UpdateCredentialControls();
    }

    private void PopulateControls()
    {
        JiraBaseUrlTextBox.Text = viewModel.JiraBaseUrl;
        JiraUserTextBox.Text = viewModel.JiraUser;
        TogglWorkspaceIdTextBox.Text = viewModel.TogglWorkspaceId?.ToString() ?? string.Empty;
        ReviewReminderTimeTextBox.Text = viewModel.ReviewReminderTime;
        EndOfDayReminderModeComboBox.ItemsSource = viewModel.ReminderModeOptions;
        EndOfDayReminderModeComboBox.SelectedValue = viewModel.EndOfDayReminderMode;
        DefaultTempoWorkCategoryTextBox.Text = viewModel.DefaultTempoWorkCategory;
        DefaultTogglProjectTextBox.Text = viewModel.DefaultTogglProject;
        AiEnabledCheckBox.IsChecked = viewModel.AiEnabled;
        AutoSyncEnabledCheckBox.IsChecked = viewModel.AutoSyncEnabled;
        SyncIntervalMinutesTextBox.Text = viewModel.SyncIntervalMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        SlackTitleTextBox.Text = viewModel.SlackTitle;
        SlackTaskHeadingTextBox.Text = viewModel.SlackTaskHeading;
        SlackExtraLinesTextBox.Text = viewModel.SlackExtraLines;
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        IsEnabled = false;
        try
        {
            if (!long.TryParse(TogglWorkspaceIdTextBox.Text.Trim(), out var workspaceId) && !string.IsNullOrWhiteSpace(TogglWorkspaceIdTextBox.Text))
                throw new ArgumentException("Enter a numeric Toggl workspace ID.");
            if (!int.TryParse(SyncIntervalMinutesTextBox.Text.Trim(), out var syncIntervalMinutes))
                throw new ArgumentException("Enter a numeric auto-sync interval in minutes.");

            var draftSettings = settings.Load() with
            {
                JiraBaseUrl = JiraBaseUrlTextBox.Text.Trim(),
                JiraUser = JiraUserTextBox.Text.Trim(),
                TogglWorkspaceId = string.IsNullOrWhiteSpace(TogglWorkspaceIdTextBox.Text) ? null : workspaceId,
                ReviewReminderTime = ReviewReminderTimeTextBox.Text.Trim(),
                EndOfDayReminderMode = EndOfDayReminderModeComboBox.SelectedValue is EndOfDayReminderMode mode ? mode : EndOfDayReminderMode.Both,
                DefaultTempoWorkCategory = DefaultTempoWorkCategoryTextBox.Text.Trim(),
                DefaultTogglProject = DefaultTogglProjectTextBox.Text.Trim(),
                AiEnabled = AiEnabledCheckBox.IsChecked == true,
                AutoSyncEnabled = AutoSyncEnabledCheckBox.IsChecked == true,
                SyncIntervalMinutes = syncIntervalMinutes,
                SlackTitle = SlackTitleTextBox.Text.Trim(),
                SlackTaskHeading = SlackTaskHeadingTextBox.Text.Trim(),
                SlackExtraLines = SlackExtraLinesTextBox.Text.Split(["\r\n", "\n"], StringSplitOptions.None)
            };
            await viewModel.SaveAsync(draftSettings, TogglTokenPasswordBox.Password, JiraTokenPasswordBox.Password, SlackWebhookPasswordBox.Password);
            TogglTokenPasswordBox.Password = string.Empty;
            JiraTokenPasswordBox.Password = string.Empty;
            SlackWebhookPasswordBox.Password = string.Empty;
            DialogResult = true;
        }
        catch (SettingsSaveException exception)
        {
            await ReloadPersistedSettingsAsync();
            System.Windows.MessageBox.Show(this, exception.Message, "GDK TimeSync", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (ArgumentException exception)
        {
            await ReloadPersistedSettingsAsync();
            System.Windows.MessageBox.Show(this, exception.Message, "GDK TimeSync", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch
        {
            await ReloadPersistedSettingsAsync();
            System.Windows.MessageBox.Show(this, "Unable to save settings.", "GDK TimeSync", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private async Task ReloadPersistedSettingsAsync()
    {
        await viewModel.LoadAsync();
        PopulateControls();
        UpdateCredentialControls();
        TogglTokenPasswordBox.Password = string.Empty;
        JiraTokenPasswordBox.Password = string.Empty;
        SlackWebhookPasswordBox.Password = string.Empty;
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
