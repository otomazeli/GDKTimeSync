using GDK.TimeSync.Desktop;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GDK.TimeSync.Tests;

public sealed class AppConfigureServicesTests
{
    [Fact]
    public void ConfigureServices_ResolvesEveryStartupCriticalServiceWithoutThrowing()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        // Mirrors what App.OnStartup resolves before the main window is shown, plus every
        // ViewModel/Service reachable from it -- a missing transitive DI dependency (e.g. an
        // Options/Configuration binding with nothing backing it) only surfaces when something
        // actually resolves the type, which production startup does but no prior test did.
        Assert.NotNull(provider.GetRequiredService<IEndOfDayReminderService>());
        Assert.NotNull(provider.GetRequiredService<ITogglAutoSyncService>());
        Assert.NotNull(provider.GetRequiredService<MainViewModel>());
        Assert.NotNull(provider.GetRequiredService<TodayViewModel>());
        Assert.NotNull(provider.GetRequiredService<TemplatesViewModel>());
        Assert.NotNull(provider.GetRequiredService<ReviewViewModel>());
        Assert.NotNull(provider.GetRequiredService<HistoryViewModel>());
        Assert.NotNull(provider.GetRequiredService<SettingsViewModel>());
        Assert.NotNull(provider.GetRequiredService<ShellViewModel>());
        Assert.NotNull(provider.GetRequiredService<ConnectionStatusViewModel>());
        Assert.NotNull(provider.GetRequiredService<ITogglSyncService>());
        Assert.NotNull(provider.GetRequiredService<IIntegrationDiagnosticsService>());
        Assert.NotNull(provider.GetRequiredService<ILiveIntegrationValidationService>());
        Assert.NotNull(provider.GetRequiredService<IConfirmedTaskDeliveryService>());
        Assert.NotNull(provider.GetRequiredService<ISlackClientFactory>());
        Assert.NotNull(provider.GetRequiredService<IConfigurationStateService>());
    }
}
