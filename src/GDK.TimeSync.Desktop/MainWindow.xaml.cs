using System.Windows;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Desktop.ViewModels;

namespace GDK.TimeSync.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly ShellViewModel shellViewModel;

    public MainWindow(MainViewModel viewModel, ShellViewModel shellViewModel)
    {
        this.viewModel = viewModel;
        this.shellViewModel = shellViewModel;
        InitializeComponent();
        VersionText.Text = AppVersion.Display;
        DataContext = shellViewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await Task.WhenAll(viewModel.InitializeAsync(), shellViewModel.InitializeAsync());

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => ((App)System.Windows.Application.Current).ShowSettings();
}
