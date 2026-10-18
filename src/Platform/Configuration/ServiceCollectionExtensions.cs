using Microsoft.Extensions.DependencyInjection;

namespace CustomsClearance.Platform.Configuration;

/// <summary>
/// Registers the configuration read service behind ConfigDbContext, same
/// AddCustomsClearance* convention as every other cross-cutting library.
/// The caller still registers ConfigDbContext itself (connection-string-
/// presence decides, as for every other DbContext in the Api host).
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCustomsClearanceConfiguration(this IServiceCollection services, TimeSpan? cacheTtl = null)
    {
        services.AddMemoryCache();
        services.AddSingleton(new ConfigCacheOptions(cacheTtl ?? ConfigCacheOptions.DefaultTtl));
        services.AddScoped<IConfigService, ConfigService>();

        return services;
    }
}
