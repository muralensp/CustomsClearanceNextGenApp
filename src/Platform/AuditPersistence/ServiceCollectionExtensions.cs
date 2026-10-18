using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CustomsClearance.Platform.Audit.Persistence;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Replaces AddCustomsClearanceAudit()'s ConsoleAuditWriter with the
    /// durable EfAuditWriter - the seam ADR-0011 left for exactly this. Call
    /// it AFTER AddCustomsClearanceAudit(). The caller supplies the provider
    /// configuration (e.g. o =&gt; o.UseNpgsql(connectionString)) so this
    /// library never references a specific database provider.
    /// </summary>
    public static IServiceCollection AddCustomsClearanceAuditPersistence(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureProvider)
    {
        services.AddDbContextFactory<AuditDbContext>(configureProvider);
        services.Replace(ServiceDescriptor.Singleton<IAuditWriter, EfAuditWriter>());
        services.TryAddSingleton<AuditQueryService>();

        return services;
    }
}
