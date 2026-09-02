using Microsoft.Extensions.DependencyInjection;
using GDK.TimeSync.Core;

namespace GDK.TimeSync.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTimeSyncPersistence(this IServiceCollection services, string databasePath)
    {
        services.AddSingleton(new SqliteDatabase(databasePath));
        services.AddSingleton<IDailyPlanRepository, SqliteDailyPlanRepository>();
        services.AddSingleton<SqliteDeliveryAttemptRepository>();
        services.AddSingleton<IDeliveryAttemptRepository>(provider => provider.GetRequiredService<SqliteDeliveryAttemptRepository>());
        services.AddSingleton<IDeliveryHistoryRepository>(provider => provider.GetRequiredService<SqliteDeliveryAttemptRepository>());
        services.AddSingleton<IDailySlackDeliveryRepository, SqliteDailySlackDeliveryRepository>();
        services.AddSingleton<ITemplateRepository, SqliteTemplateRepository>();
        return services;
    }
}
