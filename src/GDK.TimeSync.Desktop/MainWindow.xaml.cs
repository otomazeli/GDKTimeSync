using System.Windows;
using GDK.TimeSync.Desktop.ViewModels;

namespace GDK.TimeSync.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await viewModel.InitializeAsync();

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => ((App)System.Windows.Application.Current).ShowSettings();
}