using System.Windows;
using System.IO;
using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Desktop.ViewModels;
using GDK.TimeSync.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GDK.TimeSync.Desktop;

public partial class App : System.Windows.Application
{
    private ServiceProvider? serviceProvider;
    private TrayIconService? trayIcon;
    private IEndOfDayReminderService? endOfDayReminderService;
    private ITogglAutoSyncService? togglAutoSyncService;
    private volatile bool isExiting;

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GDK", "TimeSync", "logs");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var services = new ServiceCollection();
        ConfigureServices(services);

        serviceProvider = services.BuildServiceProvider();
        var auditLog = serviceProvider.GetRequiredService<IAuditLog>();
        (auditLog as FileAuditLog)?.DeleteFilesOlderThan(14);
        auditLog.Write(AuditLevel.Info, "App", $"Started version {typeof(App).Assembly.GetName().Version} — log at {LogDirectory}");
        // Must run before StartAsync: CheckNow() inside it can raise ReviewDue synchronously,
        // and HandleReviewReminderAsync silently drops the reminder if trayIcon isn't set yet.
        InitializeTrayIcon();
        endOfDayReminderService = serviceProvider.GetRequiredService<IEndOfDayReminderService>();
        endOfDayReminderService.ReviewDue += OnReviewDue;
        endOfDayReminderService.StartAsync().GetAwaiter().GetResult();
        togglAutoSyncService = serviceProvider.GetRequiredService<ITogglAutoSyncService>();
        togglAutoSyncService.StartAsync().GetAwaiter().GetResult();
        ShowMainWindow();
    }

    internal static void ConfigureServices(IServiceCollection services)
    {
        // IssueKeyValidationOptions.BindConfiguration (in AddTimeSyncCore) requires an
        // IConfiguration to be registered, even though this desktop app has no config file
        // to bind -- an empty configuration leaves IssueKeyValidationOptions at its defaults.
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddTimeSyncCore();
        services.AddTimeSyncPersistence(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GDK TimeSync",
            "timesync.db"));
        services.AddSingleton<IAuditLog>(_ => new FileAuditLog(LogDirectory));

        services.AddHttpClient(IntegrationClientFactory.TogglHttpClientName)
            .AddHttpMessageHandler(provider => new AuditLoggingHandler(provider.GetRequiredService<IAuditLog>(), IntegrationClientFactory.TogglHttpClientName));
        services.AddHttpClient(IntegrationClientFactory.JiraHttpClientName)
            .AddHttpMessageHandler(provider => new AuditLoggingHandler(provider.GetRequiredService<IAuditLog>(), IntegrationClientFactory.JiraHttpClientName));
        services.AddHttpClient(IntegrationClientFactory.TempoHttpClientName)
            .AddHttpMessageHandler(provider => new AuditLoggingHandler(provider.GetRequiredService<IAuditLog>(), IntegrationClientFactory.TempoHttpClientName));
        services.AddHttpClient(SlackClientFactory.HttpClientName)
            .AddHttpMessageHandler(provider => new AuditLoggingHandler(provider.GetRequiredService<IAuditLog>(), SlackClientFactory.HttpClientName, redactUri: true));
        services.AddSingleton<UserSettingsService>();
        services.AddSingleton<IUserSettingsStore>(provider => provider.GetRequiredService<UserSettingsService>());
        services.AddSingleton<IAiConsentService, AiConsentService>();
        services.AddSingleton<IAssistedTextGenerator, UnavailableAssistedTextGenerator>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IEndOfDayReminderService, EndOfDayReminderService>();
        services.AddSingleton<WindowsCredentialStore>();
        services.AddSingleton<ICredentialStore>(provider => provider.GetRequiredService<WindowsCredentialStore>());
        services.AddSingleton<IIntegrationClientFactory>(provider => new IntegrationClientFactory(
            provider.GetRequiredService<ICredentialStore>(),
            provider.GetRequiredService<IUserSettingsStore>(),
            null,
            null,
            provider));
        services.AddSingleton<IIntegrationDiagnosticsService, IntegrationDiagnosticsService>();
        services.AddSingleton<ILiveIntegrationValidationService, LiveIntegrationValidationService>();
        services.AddSingleton<IConfirmedTaskDeliveryService, ConfirmedTaskDeliveryService>();
        services.AddSingleton<ISlackClientFactory, SlackClientFactory>();
        services.AddSingleton<ITogglSyncService, TogglSyncService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<ITogglAutoSyncService>(provider => new TogglAutoSyncService(
            provider.GetRequiredService<MainViewModel>(),
            provider.GetRequiredService<TodayViewModel>(),
            provider.GetRequiredService<ITogglSyncService>(),
            provider.GetRequiredService<IDailyPlanRepository>(),
            provider.GetRequiredService<IUserSettingsStore>(),
            provider.GetRequiredService<TimeProvider>(),
            // Auto-sync ticks after the first resume off the UI thread; SyncNowAsync mutates a
            // UI-bound ObservableCollection, so it must be marshaled back onto the dispatcher.
            action => System.Windows.Application.Current.Dispatcher.InvokeAsync(action).Task.Unwrap()));
        services.AddSingleton<ConnectionStatusViewModel>();
        services.AddSingleton<IConfigurationStateService, ConfigurationStateService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<TodayViewModel>();
        services.AddSingleton<TemplatesViewModel>();
        RegisterReviewServices(services);
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton(_ => new AuditLogReader(LogDirectory));
        services.AddSingleton<DiagnosticsViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddTransient<SettingsWindow>();
    }

    internal static void RegisterReviewServices(IServiceCollection services) =>
        services.AddSingleton<ReviewViewModel>(provider => new ReviewViewModel(
            provider.GetRequiredService<TodayViewModel>(),
            provider.GetRequiredService<IConfirmedTaskDeliveryService>(),
            provider.GetRequiredService<IDeliveryAttemptRepository>(),
            provider.GetRequiredService<IDailySlackDeliveryRepository>(),
            provider.GetRequiredService<ISlackClientFactory>(),
            provider.GetRequiredService<IUserSettingsStore>(),
            provider.GetRequiredService<IIntegrationDiagnosticsService>(),
            provider.GetRequiredService<ILiveIntegrationValidationService>(),
            provider.GetRequiredService<IClipboardService>(),
            provider.GetRequiredService<IAuditLog>()));

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            ReminderLifecycle.BeginStop(DetachReminderService(), OnReviewDue);
        }
        finally
        {
            try
            {
                _ = StopAutoSyncIgnoringFailureAsync();
            }
            finally
            {
                try
                {
                    trayIcon?.Dispose();
                }
                catch (Exception) { }
                finally
                {
                    try
                    {
                        serviceProvider?.Dispose();
                    }
                    catch (Exception) { }
                    finally
                    {
                        base.OnExit(e);
                    }
                }
            }
        }
    }

    private ITogglAutoSyncService? DetachAutoSyncService()
    {
        var service = togglAutoSyncService;
        togglAutoSyncService = null;
        return service;
    }

    private async Task StopAutoSyncIgnoringFailureAsync()
    {
        var service = DetachAutoSyncService();
        if (service is null) return;
        try
        {
            await service.StopAsync().ConfigureAwait(false);
        }
        catch (Exception) { }
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
        serviceProvider?.GetService<IAuditLog>()?.Write(AuditLevel.Info, "App", "Shutting down");
        isExiting = true;
        await StopAutoSyncIgnoringFailureAsync();
        await ReminderLifecycle.StopThenAsync(DetachReminderService(), OnReviewDue, FlushAndShutdownAsync);
    }

    private void InitializeTrayIcon()
    {
        if (trayIcon is not null || serviceProvider is null || isExiting) return;
        try
        {
            trayIcon = new TrayIconService(ShowMainWindow, ShowSettings, ExitApplication, serviceProvider.GetRequiredService<MainViewModel>().SyncNowCommand);
        }
        catch
        {
            trayIcon = null;
        }
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

        await ReviewReminderPresenter.PresentAsync(mode, trayIcon.ShowReviewReminder, async () =>
        {
            ShowMainWindow();
            // The reminder means "review the day that just ended" -- snap back to real-today in case
            // Today was left showing a past date, so the reminder never silently shows a stale review.
            await serviceProvider.GetRequiredService<TodayViewModel>().SelectDateAsync(DateOnly.FromDateTime(DateTime.Today));
            await serviceProvider.GetRequiredService<ShellViewModel>().NavigateAsync(NavigationPage.Review);
        });
    }

    private IEndOfDayReminderService? DetachReminderService()
    {
        var reminder = endOfDayReminderService;
        endOfDayReminderService = null;
        return reminder;
    }

    private async Task FlushAndShutdownAsync()
    {
        try
        {
            if (serviceProvider is not null)
                await serviceProvider.GetRequiredService<ShellViewModel>().FlushAsync();
        }
        catch (Exception) { }
        finally
        {
            try
            {
                Shutdown();
            }
            catch (Exception) { }
        }
    }
}
