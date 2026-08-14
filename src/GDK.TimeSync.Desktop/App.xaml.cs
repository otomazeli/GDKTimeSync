using System.Windows;
using System.IO;
using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Desktop.ViewModels;
using GDK.TimeSync.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GDK.TimeSync.Desktop;

public partial class App : System.Windows.Application
{
    private ServiceProvider? serviceProvider;
    private TrayIconService? trayIcon;
    private IEndOfDayReminderService? endOfDayReminderService;
    private volatile bool isExiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var services = new ServiceCollection();
        services.AddTimeSyncCore();
        services.AddTimeSyncPersistence(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GDK TimeSync",
            "timesync.db"));
        services.AddHttpClient(IntegrationClientFactory.TogglHttpClientName);
        services.AddHttpClient(IntegrationClientFactory.JiraHttpClientName);
        services.AddHttpClient(IntegrationClientFactory.TempoHttpClientName);
        services.AddHttpClient(SlackClientFactory.HttpClientName);
        services.AddSingleton<UserSettingsService>();
        services.AddSingleton<IUserSettingsStore>(provider => provider.GetRequiredService<UserSettingsService>());
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IEndOfDayReminderService, EndOfDayReminderService>();
        services.AddSingleton<WindowsCredentialStore>();
        services.AddSingleton<ICredentialStore>(provider => provider.GetRequiredService<WindowsCredentialStore>());
        services.AddSingleton<IIntegrationClientFactory, IntegrationClientFactory>();
        services.AddSingleton<IConfirmedTaskDeliveryService, ConfirmedTaskDeliveryService>();
        services.AddSingleton<ISlackClientFactory, SlackClientFactory>();
        services.AddSingleton<IConfigurationStateService, ConfigurationStateService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<TodayViewModel>();
        services.AddSingleton<TemplatesViewModel>();
        RegisterReviewServices(services);
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddTransient<SettingsWindow>();

        serviceProvider = services.BuildServiceProvider();
        trayIcon = new TrayIconService(ShowMainWindow, ShowSettings, ExitApplication, serviceProvider.GetRequiredService<MainViewModel>().SyncNowCommand);
        endOfDayReminderService = serviceProvider.GetRequiredService<IEndOfDayReminderService>();
        endOfDayReminderService.ReviewDue += OnReviewDue;
        endOfDayReminderService.StartAsync().GetAwaiter().GetResult();
        ShowMainWindow();
    }

    internal static void RegisterReviewServices(IServiceCollection services) =>
        services.AddSingleton<ReviewViewModel>(provider => new ReviewViewModel(
            provider.GetRequiredService<TodayViewModel>(),
            provider.GetRequiredService<IConfirmedTaskDeliveryService>(),
            provider.GetRequiredService<IDeliveryAttemptRepository>(),
            provider.GetRequiredService<IDailySlackDeliveryRepository>(),
            provider.GetRequiredService<ISlackClientFactory>(),
            provider.GetRequiredService<IUserSettingsStore>()));

    protected override void OnExit(ExitEventArgs e)
    {
        StopEndOfDayReminderAsync().GetAwaiter().GetResult();
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

    internal void ShowSettings()
    {
        var window = serviceProvider!.GetRequiredService<SettingsWindow>();
        window.Owner = Current.MainWindow;
        window.ShowDialog();
    }

    private async void ExitApplication()
    {
        if (isExiting) return;
        isExiting = true;
        await StopEndOfDayReminderAsync();
        await serviceProvider!.GetRequiredService<ShellViewModel>().FlushAsync();
        Shutdown();
    }

    private void OnReviewDue(object? sender, ReviewDueEventArgs e)
    {
        if (isExiting || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        _ = DispatchReviewReminderAsync(e.Mode);
    }

    private async Task DispatchReviewReminderAsync(EndOfDayReminderMode mode)
    {
        try
        {
            await Dispatcher.InvokeAsync(() => HandleReviewReminderAsync(mode)).Task.Unwrap();
        }
        catch (Exception) when (isExiting || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) { }
        catch (Exception) { }
    }

    private async Task HandleReviewReminderAsync(EndOfDayReminderMode mode)
    {
        if (isExiting || trayIcon is null || serviceProvider is null) return;

        var actions = ReviewReminderActions.From(mode);
        if (actions.ShowTrayNotification) trayIcon.ShowReviewReminder();
        if (!actions.OpenReviewWindow) return;

        ShowMainWindow();
        await serviceProvider.GetRequiredService<ShellViewModel>().NavigateAsync(NavigationPage.Review);
    }

    private async Task StopEndOfDayReminderAsync()
    {
        if (endOfDayReminderService is null) return;

        endOfDayReminderService.ReviewDue -= OnReviewDue;
        await endOfDayReminderService.StopAsync();
        endOfDayReminderService = null;
    }
}
