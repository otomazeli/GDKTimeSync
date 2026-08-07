using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GDK.TimeSync.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTimeSyncCore(this IServiceCollection services)
    {
        services.AddOptions<IssueKeyValidationOptions>().BindConfiguration("IssueKeyValidation");
        services.AddSingleton<IssueKeyValidator>(provider =>
            new IssueKeyValidator(provider.GetRequiredService<IOptions<IssueKeyValidationOptions>>().Value));
        services.AddSingleton<TimeEntryParser>();
        return services;
    }
}
