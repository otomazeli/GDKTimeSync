using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GDK.TimeSync.Tempo;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTimeSyncTempo(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TempoOptions>()
            .Configure(options =>
            {
                options.BaseUrl = configuration["Tempo:BaseUrl"] ?? configuration["Jira:BaseUrl"] ?? options.BaseUrl;
                options.PersonalAccessToken = configuration["Tempo:PersonalAccessToken"] ?? configuration["Jira:PersonalAccessToken"] ?? string.Empty;
            })
            .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _), "Tempo base URL must be an absolute URL.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.PersonalAccessToken), "A Tempo or Jira personal access token must be configured.")
            .ValidateOnStart();

        services.AddSingleton<TempoOptions>(provider => provider.GetRequiredService<IOptions<TempoOptions>>().Value);
        services.AddHttpClient<TempoClient>();
        return services;
    }
}
