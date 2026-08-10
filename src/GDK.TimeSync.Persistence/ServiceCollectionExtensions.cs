using Microsoft.Extensions.DependencyInjection;
using GDK.TimeSync.Core;

namespace GDK.TimeSync.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTimeSyncPersistence(this IServiceCollection services, string databasePath)
    {
        services.AddSingleton(new SqliteDatabase(databasePath));
        services.AddSingleton<IDailyPlanRepository, SqliteDailyPlanRepository>();
        services.AddSingleton<ITemplateRepository, SqliteTemplateRepository>();
        return services;
    }
}
