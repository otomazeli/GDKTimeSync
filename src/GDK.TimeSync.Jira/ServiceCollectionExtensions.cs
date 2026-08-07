using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GDK.TimeSync.Jira;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTimeSyncJira(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JiraOptions>()
            .Bind(configuration.GetSection("Jira"))
            .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _), "Jira:BaseUrl must be an absolute URL.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.PersonalAccessToken), "Jira:PersonalAccessToken must be configured.")
            .ValidateOnStart();

        services.AddHttpClient<JiraClient>();
        return services;
    }
}
