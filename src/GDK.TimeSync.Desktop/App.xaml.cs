using System.Windows;
using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GDK.TimeSync.Desktop;

public partial class App : System.Windows.Application
{
    private ServiceProvider? serviceProvider;
    private TrayIconService? trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var services = new ServiceCollection();
        services.AddTimeSyncCore();
        services.AddHttpClient();
        services.AddSingleton<UserSettingsService>();
        services.AddSingleton<WindowsCredentialStore>();
        services.AddSingleton<MainWindow>();

        serviceProvider = services.BuildServiceProvider();
        trayIcon = new TrayIconService(ShowMainWindow, ShowSettings, ExitApplication);
        ShowMainWindow();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        trayIcon?.Dispose();
        serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private void ShowMainWindow()
    {
        var window = serviceProvider!.GetRequiredService<MainWindow>();
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private void ShowSettings()
    {
        var settings = serviceProvider!.GetRequiredService<UserSettingsService>();
        var credentials = serviceProvider!.GetRequiredService<WindowsCredentialStore>();
        var window = new SettingsWindow(settings, credentials) { Owner = Current.MainWindow };
        window.ShowDialog();
    }

    private void ExitApplication() => Shutdown();
}