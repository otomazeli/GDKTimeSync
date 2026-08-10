namespace GDK.TimeSync.Desktop.Views;

public partial class SettingsView : System.Windows.Controls.UserControl
{
    public SettingsView() => InitializeComponent();

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.SettingsViewModel viewModel)
            await viewModel.LoadAsync();
    }

    private void OnOpenSettingsClick(object sender, System.Windows.RoutedEventArgs e) => ((App)System.Windows.Application.Current).ShowSettings();
}
